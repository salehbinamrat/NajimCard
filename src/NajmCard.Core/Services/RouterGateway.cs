using System.Globalization;
using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

/// <summary>RouterOS 6 Hotspot gateway. Router write operations must be explicitly requested by UI.</summary>
public sealed record ExpiredTransferDeletionResult(int Requested, int Deleted, int Skipped,
    IReadOnlyList<string> Failed, bool UncertainOutcome)
{
    // Reuse the one authoritative pre-delete read, with only successfully removed
    // IDs excluded. Unknown outcomes deliberately expose no refreshed snapshot.
    public IReadOnlyList<UserManagerV6Voucher>? RemainingUserManager { get; init; }
    public IReadOnlyList<HotspotUser>? RemainingHotspot { get; init; }
}
public sealed record ExpiredTransferDeletionProgress(int Processed,int Total,
    int Deleted,int Skipped,int Failed);

public sealed partial class RouterGateway(IRouterCommandClient client)
{
    private static string Get(IReadOnlyDictionary<string, string> row, string key) =>
        row.TryGetValue(key, out string? value) ? value : "";
    private static long Long(IReadOnlyDictionary<string, string> row, string key) =>
        long.TryParse(Get(row, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out long n) ? n : 0;
    private static bool Disabled(IReadOnlyDictionary<string, string> row) =>
        Get(row, "disabled") is "true" or "yes" or "1";

    private string _lastRouterIdentity = "";

    public Task<RouterResource> ReadResourceAsync(CancellationToken ct = default)
        => ReadResourceCoreAsync(true, ct);

    public Task<RouterResource> ReadTelemetryResourceAsync(CancellationToken ct = default)
        => ReadResourceCoreAsync(false, ct);

    private async Task<RouterResource> ReadResourceCoreAsync(bool refreshIdentity, CancellationToken ct)
    {
        if (refreshIdentity)
        {
            var identity = (await client.CommandAsync("/system/identity/print", cancellationToken: ct)).FirstOrDefault();
            _lastRouterIdentity = identity is null ? "" : Get(identity, "name");
        }
        var resource = (await client.CommandAsync("/system/resource/print", cancellationToken: ct)).FirstOrDefault();
        if (resource is null) throw new IOException("RouterOS returned no resource data");
        return new RouterResource(_lastRouterIdentity, Get(resource, "board-name"),
            Get(resource, "version"), Get(resource, "cpu-load"), Get(resource, "free-memory"),
            Get(resource, "total-memory"), Get(resource, "uptime"));
    }

    public async Task<IReadOnlyList<HotspotUser>> ReadUsersAsync(CancellationToken ct = default)
    {
        var rows = await client.CommandAsync("/ip/hotspot/user/print", cancellationToken: ct);
        return rows.Select(row => new HotspotUser(
            Get(row, ".id"), Get(row, "name"), Get(row, "password"), Get(row, "profile"),
            Disabled(row), Long(row, "bytes-in"), Long(row, "bytes-out"),
            Long(row, "limit-bytes-total"), Get(row, "comment"))).ToList();
    }

    /// <summary>
    /// Read all installed authentication services BEFORE issuing new vouchers.
    /// A local Hotspot/PPP account can shadow a Radius/User Manager voucher.
    /// Missing menu may be skipped; permission/network errors fail closed.
    /// </summary>
    public async Task<IReadOnlySet<string>> ReadExistingVoucherNamesAsync(
        CancellationToken ct = default)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sources = new (string Path, string Field)[]
        {
            ("/ip/hotspot/user/print", "name"),
            ("/ppp/secret/print", "name"),
            ("/tool/user-manager/user/print", "username")
        };
        foreach (var (path, field) in sources)
        {
            IReadOnlyList<IReadOnlyDictionary<string, string>> rows;
            try
            {
                rows = await client.CommandAsync(path,
                    new Dictionary<string,string> { [".proplist"] = field }, ct);
            }
            catch (RouterOsTrapException ex) when (
                ex.Message.Contains("no such command", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("bad command name", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("unknown command", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            // RouterOS 6 sometimes silently drops projected columns.
            // Never mistake omitted usernames for an empty service.
            if (rows.Any(row => !row.ContainsKey(field)))
                rows = await client.CommandAsync(path, cancellationToken: ct);
            if (rows.Any(row => !row.ContainsKey(field)))
                throw new InvalidDataException(
                    "لم يعد RouterOS أسماء المستخدمين المطلوبة؛ أُوقف الإصدار دون أي تعديل.");

            foreach (var row in rows)
                if (row.TryGetValue(field, out var value) &&
                    !string.IsNullOrWhiteSpace(value))
                    names.Add(value);
        }
        return names;
    }

    /// <summary>
    /// Recheck one selected UM6 account using ?.id before any mutation.
    /// A name, profile, disabled-state or internal ID mismatch aborts the operation.
    /// Verify resulting state after the write; do not retry ambiguous responses.
    /// </summary>
    public async Task<UserManagerV6MutationResult> ChangeUserManagerV6UserAsync(
        UserManagerV6Voucher selected, string action, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(selected);
        if (action is not ("enable" or "disable" or "remove"))
            throw new ArgumentException("عملية User Manager غير معتمدة.", nameof(action));
        ValidateInternalId(selected.Id);
        if (string.IsNullOrWhiteSpace(selected.Username))
            throw new ArgumentException("اسم حساب User Manager غير صالح.", nameof(selected));
        if (client is not IRouterQueryClient query)
            throw new NotSupportedException("لا يمكن تأكيد حساب User Manager دون قراءة RouterOS موجّهة بالمعرّف.");

        const string path = "/tool/user-manager/user/print";
        var projected = new Dictionary<string,string>
        {
            [".proplist"] = ".id,username,disabled,actual-profile"
        };

        async Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> ReadTarget() =>
            await query.QueryByIdAsync(path,selected.Id,projected,ct);

        var before = await ReadTarget();
        if (before.Count != 1)
            throw new InvalidOperationException("تعذر تأكيد حساب User Manager واحد؛ لم يُنفذ تعديل.");
        var current=before[0];
        if (Get(current,".id")!=selected.Id ||
            Get(current,"username")!=selected.Username ||
            !current.ContainsKey("disabled") ||
            Get(current,"actual-profile")!=selected.Profile ||
            (Get(current,"disabled") is "yes" or "true" or "1") != selected.Disabled)
            throw new InvalidOperationException("تغيرت هوية الحساب أو باقته أو حالته؛ حدّث القائمة قبل التعديل.");

        string command=action=="remove"
            ? "/tool/user-manager/user/remove"
            : "/tool/user-manager/user/set";
        var attrs=new Dictionary<string,string> { [".id"]=selected.Id };
        if (action!="remove") attrs["disabled"]=action=="disable" ? "yes" : "no";

        try
        {
            await client.CommandAsync(command,attrs,ct);
        }
        catch (RouterOsTrapException ex)
        {
            return new UserManagerV6MutationResult(false,false,
                "رفض الراوتر العملية: "+ex.Message);
        }
        catch (IOException)
        {
            return new UserManagerV6MutationResult(false,true,
                "انقطع الاتصال بعد أمر تعديل الحساب؛ راجع الراوتر قبل إعادة المحاولة.");
        }
        catch (OperationCanceledException)
        {
            return new UserManagerV6MutationResult(false,true,
                "نتيجة تعديل الحساب غير مؤكدة بعد إلغاء الانتظار.");
        }

        try
        {
            var after=await ReadTarget();
            if (action=="remove")
                return after.Count==0
                    ? new UserManagerV6MutationResult(true,false,"حُذف الحساب المحدد وتم تأكيد غيابه.")
                    : new UserManagerV6MutationResult(false,true,
                        "أجاب الراوتر بنجاح لكن لم يتأكد حذف الحساب؛ حدّث البيانات قبل أي تعديل.");
            if (after.Count==1 && Get(after[0],".id")==selected.Id
                && Get(after[0],"username")==selected.Username
                && after[0].ContainsKey("disabled")
                && (Get(after[0],"disabled") is "yes" or "true" or "1")==(action=="disable"))
                return new UserManagerV6MutationResult(true,false,
                    action=="disable"?"تم تعطيل الحساب والتحقق من حالته.":"تم تفعيل الحساب والتحقق من حالته.");
            return new UserManagerV6MutationResult(false,true,
                "استجاب الراوتر لكن الحالة بعد التعديل لا تطابق المتوقع؛ يلزم التحقق يدوياً.");
        }
        catch (Exception ex) when (
            ex is IOException or OperationCanceledException or InvalidDataException)
        {
            return new UserManagerV6MutationResult(false,true,
                "تم إرسال الأمر لكن تعذر التحقق من النتيجة: "+ex.Message);
        }
    }

    /// <summary>
    /// Read RouterOS 6 PPP secrets, profiles and current sessions separately.
    /// This includes PPPoE and any other configured PPP service, preserving
    /// the service field rather than silently dropping other PPP accounts.
    /// Passwords never enter returned UI models. Errors fail closed.
    /// </summary>
    /// <summary>
    /// Change exactly one selected PPP secret after a fresh server-side .id read.
    /// The same identity, profile, service, and state must match the UI snapshot
    /// before the write. Verify the result through a second server-side query.
    /// A lost response is uncertain; never retry it blindly.
    /// </summary>
    public async Task<PppAccountMutationResult> SetPppAccountDisabledAsync(
        PppAccount selected, bool disabled, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(selected);
        ValidateInternalId(selected.Id);
        if (string.IsNullOrWhiteSpace(selected.Name) ||
            string.IsNullOrWhiteSpace(selected.Profile) ||
            string.IsNullOrWhiteSpace(selected.Service))
            throw new ArgumentException("بيانات حساب PPP غير مكتملة؛ حدّث القائمة قبل التعديل.");
        if (client is not IRouterQueryClient targeted)
            throw new NotSupportedException(
                "التعديل يتطلب قراءة حساب PPP محدد باستخدام استعلام RouterOS مقيّد بالمعرف.");

        var projected = new Dictionary<string,string>
        {
            [".proplist"]=".id,name,profile,service,disabled"
        };
        const string path="/ppp/secret/print";

        static bool IsDisabled(string value) =>
            value.Equals("yes",StringComparison.OrdinalIgnoreCase)
            || value.Equals("true",StringComparison.OrdinalIgnoreCase)
            || value=="1";

        async Task<IReadOnlyList<IReadOnlyDictionary<string,string>>> ReadTarget() =>
            await targeted.QueryByIdAsync(path,selected.Id,projected,ct);

        var before=await ReadTarget();
        if (before.Count!=1)
            throw new InvalidOperationException("لم يؤكد الراوتر حساب PPP واحداً بالمعرف المحدد؛ لم يُنفذ أي تعديل.");

        var current=before[0];
        if (Get(current,".id")!=selected.Id ||
            Get(current,"name")!=selected.Name ||
            !current.ContainsKey("profile") || Get(current,"profile")!=selected.Profile ||
            !current.ContainsKey("service") || Get(current,"service")!=selected.Service ||
            !current.ContainsKey("disabled") || IsDisabled(Get(current,"disabled"))!=selected.Disabled)
            throw new InvalidOperationException(
                "تغيّر اسم الحساب أو باقته أو نوع خدمته أو حالته؛ حدّث قائمة PPP قبل التعديل.");

        if (selected.Disabled==disabled)
            return new PppAccountMutationResult(true,false,
                "الحساب موجود بالفعل في الحالة المطلوبة؛ لم يُرسل أمر تعديل.");

        try
        {
            await client.CommandAsync("/ppp/secret/set",
                new Dictionary<string,string>
                {
                    [".id"]=selected.Id,
                    ["disabled"]=disabled?"yes":"no"
                },ct);
        }
        catch (RouterOsTrapException ex)
        {
            return new PppAccountMutationResult(false,false,
                "رفض الراوتر تغيير حالة حساب PPP: "+ex.Message);
        }
        catch (IOException)
        {
            return new PppAccountMutationResult(false,true,
                "انقطع الاتصال بعد أمر تعديل PPP؛ قد يكون الراوتر نفذه. لا تعاود الأمر قبل مراجعة الحالة.");
        }
        catch (OperationCanceledException)
        {
            return new PppAccountMutationResult(false,true,
                "انتهى الانتظار بعد إرسال تعديل PPP؛ النتيجة غير مؤكدة.");
        }

        try
        {
            var after=await ReadTarget();
            if (after.Count==1 && Get(after[0],".id")==selected.Id
                && Get(after[0],"name")==selected.Name
                && Get(after[0],"profile")==selected.Profile
                && Get(after[0],"service")==selected.Service
                && after[0].ContainsKey("disabled")
                && IsDisabled(Get(after[0],"disabled"))==disabled)
                return new PppAccountMutationResult(true,false,
                    disabled?"تم تعطيل الحساب والتحقق من استجابة الراوتر.":
                             "تم تفعيل الحساب والتحقق من استجابة الراوتر.");
            return new PppAccountMutationResult(false,true,
                "أجاب الراوتر لكن لم تتأكد الحالة المطلوبة من قراءة جديدة؛ تحقق يدوياً.");
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException
            or InvalidDataException)
        {
            return new PppAccountMutationResult(false,true,
                "تم إرسال أمر PPP لكن تعذر التأكد من النتيجة: "+ex.Message);
        }
    }

    public async Task<PppInventory> ReadPppInventoryAsync(CancellationToken ct = default)
    {
        var secrets = await client.CommandAsync("/ppp/secret/print", cancellationToken: ct);
        var profiles = await ReadPppProfilesAsync(ct);
        var activeRows = await client.CommandAsync("/ppp/active/print", cancellationToken: ct);

        var active = activeRows.Select(row => new PppActiveSession(
            Get(row, ".id"), Get(row, "name"), Get(row, "service"),
            Get(row, "address"), Get(row, "caller-id"), Get(row, "uptime"))).ToArray();

        var activeNames = active.Select(x => x.Name)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var accounts = secrets.Select(row => new PppAccount(
            Get(row, ".id"), Get(row, "name"), Get(row, "profile"),
            Get(row, "service"),
            Get(row, "disabled") is "true" or "yes" or "1",
            activeNames.Contains(Get(row, "name")),
            Get(row, "comment"))).ToArray();

        return new PppInventory(accounts, profiles, active);
    }

    public async Task<IReadOnlyList<PppProfile>> ReadPppProfilesAsync(CancellationToken ct=default)
    {
        var rows=await client.CommandAsync("/ppp/profile/print",cancellationToken:ct);
        return rows.Select(row=>new PppProfile(
            Get(row,".id"),Get(row,"name"),Get(row,"rate-limit"),
            Get(row,"local-address"),Get(row,"remote-address"))).ToArray();
    }

    public async Task<IReadOnlyList<HotspotSession>> ReadSessionsAsync(CancellationToken ct = default)
    {
        var rows = await client.CommandAsync("/ip/hotspot/active/print", cancellationToken: ct);
        return rows.Select(row => new HotspotSession(Get(row, ".id"), Get(row, "user"),
            Get(row, "address"), Get(row, "mac-address"), Get(row, "uptime"))).ToList();
    }

    public async Task<IReadOnlyList<RouterCardProfile>> ReadProfilesAsync(CancellationToken ct = default)
    {
        var rows = await client.CommandAsync("/ip/hotspot/user/profile/print", cancellationToken: ct);
        return rows.Select(row => new RouterCardProfile(Get(row, ".id"), Get(row, "name"), Get(row, "rate-limit"),
            Get(row, "session-timeout"), Get(row, "shared-users"))).ToList();
    }

    /// <summary>
    /// Read-only RouterOS 6 User Manager profile definitions.
    /// The RouterOS 7 /user-manager path is intentionally not used.
    /// </summary>
    public async Task<IReadOnlyList<UserManagerV6Profile>> ReadUserManagerV6ProfilesAsync(
        CancellationToken ct = default)
    {
        var rows = await client.CommandAsync("/tool/user-manager/profile/print", cancellationToken: ct);
        return rows.Select(row => new UserManagerV6Profile(
            Get(row, ".id"), Get(row, "name"),
            Get(row, "owner") is { Length: > 0 } owner ? owner : Get(row, "customer"),
            Get(row, "validity"), Get(row, "price"), Get(row, "starts-at"))).ToArray();
    }

    /// <summary>Read-only RouterOS 6 User Manager customer logins.</summary>
    private static bool HasLifecycleFields(IReadOnlyDictionary<string,string> row)
    {
        string[] keys = ["actual-profile","money-used","uptime-used",
            "total-download-used","total-upload-used","download-used","upload-used",
            "transfer-limit","uptime-limit","time-left","total-time-left"];
        return keys.Any(row.ContainsKey);
    }

    private async Task<IReadOnlyDictionary<string,long>> UserManagerV6ProfileTransferLimitsAsync(
        CancellationToken ct)
    {
        // All three small definition tables are read once. Links may refer to
        // either the profile/limitation name or the internal RouterOS ID.
        var profiles=await client.CommandAsync(
            "/tool/user-manager/profile/print", cancellationToken:ct);
        var limits=await client.CommandAsync(
            "/tool/user-manager/profile/limitation/print", cancellationToken:ct);
        var links=await client.CommandAsync(
            "/tool/user-manager/profile/profile-limitation/print", cancellationToken:ct);

        var limitAliases=new Dictionary<string,long>(StringComparer.OrdinalIgnoreCase);
        foreach(var row in limits)
        {
            var value=UserManagerV6Usage.ParseBytes(Get(row,"transfer-limit"));
            foreach(var alias in new[] { Get(row,".id"),Get(row,"name") })
                if(alias.Length>0) limitAliases[alias]=value;
        }

        var profileAliases=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var row in profiles)
        {
            var canonical=Get(row,"name");
            if(canonical.Length==0) canonical=Get(row,".id");
            if(canonical.Length==0) continue;
            profileAliases[canonical]=canonical;
            var id=Get(row,".id");
            if(id.Length>0) profileAliases[id]=canonical;
        }
        var totals=new Dictionary<string,long>(StringComparer.OrdinalIgnoreCase);
        foreach(var link in links)
        {
            if (!profileAliases.TryGetValue(Get(link,"profile"),out var canonical)
                || !limitAliases.TryGetValue(Get(link,"limitation"),out var limit)
                || limit<=0) continue;
            if(!totals.TryGetValue(canonical,out var old) || limit<old)
                totals[canonical]=limit;
        }
        var expanded=new Dictionary<string,long>(StringComparer.OrdinalIgnoreCase);
        foreach(var pair in profileAliases)
            if(totals.TryGetValue(pair.Value,out var value))
                expanded[pair.Key]=value;
        return expanded;
    }

    /// <summary>
    /// On-demand authoritative RouterOS 6 inventory. Never run in the background
    /// on ordinary navigation. Dynamic columns may be omitted from .proplist:
    /// retry one full read instead of accepting misleading zero metrics.
    /// </summary>
    public async Task<UserManagerV6Inventory> ReadUserManagerV6InventoryAsync(
        CancellationToken ct = default)
    {
        const string path="/tool/user-manager/user/print";
        const string projection=
            ".id,username,name,disabled,incomplete,actual-profile,money-used,uptime-used,"+
            "total-download-used,total-upload-used,transfer-limit,last-used-ip,last-used-mac,"+
            "active,active-sessions,last-seen";

        IReadOnlyList<IReadOnlyDictionary<string,string>> rows;
        try
        {
            rows=await client.CommandAsync(path,
                new Dictionary<string,string> { [".proplist"]=projection },ct);
        }
        catch(RouterOsTrapException ex) when (
            ex.Message.Contains("unknown parameter",StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("invalid argument",StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("no such argument",StringComparison.OrdinalIgnoreCase))
        {
            rows=await client.CommandAsync(path,cancellationToken:ct);
        }

        if(rows.Count>0&&!rows.Any(HasLifecycleFields))
            rows=await client.CommandAsync(path,cancellationToken:ct);

        var observed=rows.Any(HasLifecycleFields);
        IReadOnlyDictionary<string,long> limitMap=new Dictionary<string,long>();
        string warning="";
        try { limitMap=await UserManagerV6ProfileTransferLimitsAsync(ct); }
        catch (RouterOsTrapException ex)
        {
            warning="تعذرت قراءة تعريفات حدود الباقات: "+ex.Message;
        }

        var users=new List<UserManagerV6Voucher>(rows.Count);
        foreach(var row in rows)
        {
            var profile=Get(row,"actual-profile");
            limitMap.TryGetValue(profile,out var profileLimit);
            users.Add(UserManagerV6Usage.Classify(row,profileLimit));
        }

        bool complete=rows.Count==0 || observed;
        if (rows.Count>0 && !complete)
            warning="بيانات دورة حياة User Manager غير مكتملة؛ لا تعتمد أعداداً صفرية أو حذفاً تلقائياً. "
                +warning;
        return new UserManagerV6Inventory(users,complete,warning);
    }

    public async Task<IReadOnlyList<UserManagerV6Customer>> ReadUserManagerV6CustomersAsync(
        CancellationToken ct = default)
    {
        var rows = await client.CommandAsync("/tool/user-manager/customer/print", cancellationToken: ct);
        return rows.Select(row => new UserManagerV6Customer(
            Get(row, ".id"),
            Get(row, "login") is { Length: > 0 } login ? login : Get(row, "name"),
            Get(row, "name"))).ToArray();
    }

    public async Task<IReadOnlyList<RouterHotspotServer>> ReadServersAsync(CancellationToken ct = default)
    {
        var rows = await client.CommandAsync("/ip/hotspot/print", cancellationToken: ct);
        return rows.Select(row => new RouterHotspotServer(Get(row, ".id"), Get(row, "name"),
            Get(row, "interface"), Disabled(row))).ToList();
    }

    private static void ValidateInternalId(string id)
    {
        if (id.Length is < 2 or > 40 || id[0] != '*' || id.Skip(1).Any(c => !char.IsAsciiHexDigit(c)))
            throw new ArgumentException("حدد السجل الصحيح من الراوتر أولاً.");
    }

    public async Task SetUserDisabledAsync(string routerId, bool disabled, CancellationToken ct = default)
    {
        ValidateInternalId(routerId);
        await client.CommandAsync("/ip/hotspot/user/set", new Dictionary<string, string>
        {
            [".id"] = routerId, ["disabled"] = disabled ? "yes" : "no"
        }, ct);
    }

    public async Task DeleteUserAsync(string routerId, CancellationToken ct = default)
    {
        ValidateInternalId(routerId);
        await client.CommandAsync("/ip/hotspot/user/remove", new Dictionary<string, string> { [".id"] = routerId }, ct);
    }

    /// <summary>
    /// Deletes only users already selected by a confirmed UI snapshot AND still expired
    /// by transfer limit in a single fresh RouterOS read. No retries on uncertain writes.
    /// </summary>
    public async Task<ExpiredTransferDeletionResult> DeleteExpiredByTransferAsync(
        IReadOnlyList<HotspotUser> confirmedSnapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(confirmedSnapshot);
        var requested = confirmedSnapshot.Where(x => x.IsExpiredByTransferLimit).ToArray();
        if (requested.Length == 0)
            return new ExpiredTransferDeletionResult(0, 0, 0, [], false);

        // Recheck once immediately before writing; never rely only on stale UI state.
        var fresh = (await ReadUsersAsync(ct)).ToDictionary(x => x.Id, StringComparer.Ordinal);
        var verified = new List<HotspotUser>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var skipped = 0;
        foreach (var old in requested)
        {
            if (!seenIds.Add(old.Id)
                || !fresh.TryGetValue(old.Id, out var now)
                || !string.Equals(now.Name, old.Name, StringComparison.Ordinal)
                || !string.Equals(now.Profile, old.Profile, StringComparison.Ordinal)
                || now.TransferLimit != old.TransferLimit
                || !now.IsExpiredByTransferLimit)
            {
                skipped++;
                continue;
            }
            verified.Add(now);
        }

        var failed = new List<string>();
        var deleted = 0;
        var confirmedRemoved=new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in verified)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await DeleteUserAsync(item.Id, ct);
                deleted++;
                confirmedRemoved.Add(item.Id);
            }
            catch (RouterOsTrapException)
            {
                failed.Add(item.Name);
            }
            catch (Exception ex) when(ex is IOException or TimeoutException
                or OperationCanceledException or ObjectDisposedException)
            {
                // Unacknowledged mutation may already have executed. Never auto-retry.
                return new ExpiredTransferDeletionResult(requested.Length,deleted,
                    skipped,failed,true);
            }
            catch (ArgumentException)
            {
                // Reject malformed IDs rather than sending unsafe RouterOS commands.
                failed.Add(item.Name);
            }
        }

        return new ExpiredTransferDeletionResult(requested.Length, deleted, skipped, failed, false)
        {
            RemainingHotspot=fresh.Values
                .Where(x=>!confirmedRemoved.Contains(x.Id)).ToArray()
        };
    }

    /// <summary>
    /// Delete ONLY previously confirmed RouterOS 6 UM vouchers that remain
    /// expired by a positive exhausted Transfer Limit on one fresh full scan.
    /// Do not use Total Time Left or a missing actual profile to prove expiry.
    /// No per-card full scans, no blind retry after a lost write response.
    /// </summary>
    public async Task<ExpiredTransferDeletionResult> DeleteExpiredUserManagerV6Async(
        IReadOnlyList<UserManagerV6Voucher> confirmedSnapshot,
        CancellationToken ct = default,
        IProgress<ExpiredTransferDeletionProgress>? progress=null)
    {
        ArgumentNullException.ThrowIfNull(confirmedSnapshot);
        static bool IsExhausted(UserManagerV6Voucher item) =>
            item.Status=="expired" && item.ExpirySource=="transfer-limit"
            && item.Started && !item.Disabled && !item.Incomplete
            && item.TransferLimit>0 && item.ConsumedBytes>=item.TransferLimit;

        var requested=confirmedSnapshot.Where(IsExhausted).ToArray();
        if(requested.Length==0)
            return new ExpiredTransferDeletionResult(0,0,0,[],false);

        // Fail closed: never delete based on an incomplete RouterOS 6 projection.
        var live=await ReadUserManagerV6InventoryAsync(ct);
        if(!live.IsComplete)
            throw new InvalidOperationException(
                "قراءة حالات User Manager غير مكتملة؛ لم يُحذف أي كرت.");

        var byId=live.Users.GroupBy(x=>x.Id,StringComparer.Ordinal)
            .ToDictionary(g=>g.Key,g=>g.ToArray(),StringComparer.Ordinal);
        var verified=new List<UserManagerV6Voucher>();
        var seen=new HashSet<string>(StringComparer.Ordinal);
        int skipped=0;
        foreach(var old in requested)
        {
            if(old.Id.Length<2 || old.Id.Length>40 || old.Id[0]!='*'
                || old.Id.Skip(1).Any(c=>!char.IsAsciiHexDigit(c))
                || !seen.Add(old.Id)
                || !byId.TryGetValue(old.Id,out var matches)
                || matches.Length!=1)
            {
                skipped++;
                continue;
            }
            var fresh=matches[0];
            if(fresh.Username!=old.Username
                || fresh.Profile!=old.Profile
                || fresh.TransferLimit!=old.TransferLimit
                || !IsExhausted(fresh))
            {
                skipped++;
                continue;
            }
            verified.Add(fresh);
        }

        var deleted=0;
        var failed=new List<string>();
        var deletedIds=new HashSet<string>(StringComparer.Ordinal);
        int processed=0;
        progress?.Report(new ExpiredTransferDeletionProgress(0,verified.Count,0,skipped,0));
        foreach(var voucher in verified)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await client.CommandAsync(
                    "/tool/user-manager/user/remove",
                    new Dictionary<string,string> { [".id"]=voucher.Id },ct);
                deleted++;
                deletedIds.Add(voucher.Id);
            }
            catch(RouterOsTrapException)
            {
                failed.Add(voucher.Username);
            }
            catch(Exception ex) when(ex is IOException or TimeoutException
                or OperationCanceledException or ObjectDisposedException)
            {
                // The last remove may have happened even without an acknowledgement.
                return new ExpiredTransferDeletionResult(
                    requested.Length,deleted,skipped,failed,true);
            }
            processed++;
            // Throttle UI progress updates while retaining per-ID acknowledgement.
            if(processed%20==0 || processed==verified.Count)
                progress?.Report(new ExpiredTransferDeletionProgress(
                    processed,verified.Count,deleted,skipped,failed.Count));
        }
        return new ExpiredTransferDeletionResult(
            requested.Length,deleted,skipped,failed,false)
        {
            RemainingUserManager=live.Users
                .Where(x=>!deletedIds.Contains(x.Id)).ToArray()
        };
    }

    /// <summary>
    /// Strict RouterOS 6 UM creation contract: add username/password/customer, then
    /// apply a profile to the exact new internal ID returned in !done =ret=.
    /// Never retry an ambiguous write. Only an explicit activation trap permits
    /// cleanup, which is restricted to the ID just returned by /add.
    /// </summary>
    public async Task<UserManagerV6DeliveryResult> CreateAndActivateUserManagerV6Async(
        Voucher voucher, string customer, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(voucher);
        ct.ThrowIfCancellationRequested();
        if (voucher.Username.Length is < 1 or > 64
            || voucher.Username.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)))
            throw new ArgumentException("اسم كرت User Manager غير صالح.", nameof(voucher));
        // A genuinely username-only voucher has Password=="". RouterOS v6
        // accepts an explicit empty password field; never replace it with
        // the username behind the operator's back.
        if (voucher.Password is null || voucher.Password.Length > 128)
            throw new ArgumentException("كلمة مرور كرت User Manager غير صالحة.", nameof(voucher));
        if (string.IsNullOrWhiteSpace(voucher.Profile) || voucher.Profile.Length > 100)
            throw new ArgumentException("اختر باقة User Manager صالحة.", nameof(voucher));
        if (string.IsNullOrWhiteSpace(customer) || customer.Length > 100)
            throw new ArgumentException("اختر حساب مدير User Manager v6 أولاً.", nameof(customer));
        if (client is not IRouterReceiptClient receiptClient)
            throw new NotSupportedException("RouterOS receipt-aware transport is required for safe UM issuing.");

        RouterCommandReply created;
        try
        {
            created = await receiptClient.CommandWithReceiptAsync(
                "/tool/user-manager/user/add",
                new Dictionary<string,string>
                {
                    ["username"] = voucher.Username,
                    ["password"] = voucher.Password,
                    ["customer"] = customer
                }, ct);
        }
        catch (RouterOsTrapException ex)
        {
            return new UserManagerV6DeliveryResult(false, false, null,
                "رفض الراوتر إنشاء الكرت: " + ex.Message);
        }
        catch (IOException)
        {
            return new UserManagerV6DeliveryResult(false, true, null,
                "انقطع الاتصال أثناء إنشاء حساب User Manager؛ راجع الراوتر قبل أي إعادة محاولة.");
        }
        catch (OperationCanceledException)
        {
            return new UserManagerV6DeliveryResult(false, true, null,
                "توقفت العملية أثناء إنشاء الحساب؛ قد يكون الراوتر استقبلها.");
        }

        created.Done.TryGetValue("ret", out var id);
        if (string.IsNullOrEmpty(id) || id.Length > 40 || id[0] != '*'
            || id.Length == 1 || id.Skip(1).Any(c => !char.IsAsciiHexDigit(c)))
        {
            return new UserManagerV6DeliveryResult(false, true, null,
                "قد يكون الحساب أُنشئ دون أن يرجع الراوتر معرفاً وحيداً؛ لم نربط الباقة ولم نعد المحاولة.");
        }

        try
        {
            await client.CommandAsync(
                "/tool/user-manager/user/create-and-activate-profile",
                new Dictionary<string,string>
                {
                    ["numbers"] = id,
                    ["customer"] = customer,
                    ["profile"] = voucher.Profile
                }, ct);
            // Hadramout uses /add followed by create-and-activate-profile.
            // That sequence is necessary, but !done only confirms RouterOS
            // accepted the command. A voucher may still be absent or lack
            // an actual profile. Do not mark it SENT/printable until a live
            // exact-ID read confirms both the username and assigned profile.
            // Never replay either write, even if this read is unavailable.
            // RouterOS readback failures are NOT activation traps.
            // Never roll back an account after activation has been ACKed.
            try
            {
                if (client is not IRouterQueryClient lookup)
                    return new UserManagerV6DeliveryResult(false, true, id,
                        "قبل الراوتر أمر الربط، لكن لا تتوفر قراءة للتحقق من الحساب؛ لم يُعتمد للطباعة.");
                const string verifyPath = "/tool/user-manager/user/print";
                var projection = new Dictionary<string,string>
                {
                    [".proplist"] = ".id,username,actual-profile,customer"
                };
                string? lastVerificationError = null;
                for (int attempt=0;attempt<3;attempt++)
                {
                    if(attempt>0)await Task.Delay(attempt==1?150:300,ct);
                    var found=await lookup.QueryByIdAsync(verifyPath,id,projection,ct);
                    // Older RouterOS 6 builds sometimes omit projected columns.
                    // Retry the same ID with all fields, never read the full table.
                    if(found.Count==1 && (!found[0].ContainsKey("username")
                        || !found[0].ContainsKey("actual-profile")))
                        found=await lookup.QueryByIdAsync(verifyPath,id,cancellationToken:ct);
                    if(found.Count!=1)
                    {
                        lastVerificationError="لم يرجع الراوتر حسابا واحدا بالمعرف الذي أُنشئ.";
                        continue;
                    }
                    var actual=found[0];
                    if(Get(actual,".id")!=id ||
                       !string.Equals(Get(actual,"username"),voucher.Username,
                           StringComparison.Ordinal))
                    {
                        lastVerificationError="هوية الحساب المقروء لا تطابق رقم الكرت الذي أُنشئ.";
                        break;
                    }
                    if(actual.TryGetValue("customer",out var reportedCustomer) &&
                       !string.IsNullOrWhiteSpace(reportedCustomer) &&
                       !string.Equals(reportedCustomer,customer,StringComparison.OrdinalIgnoreCase))
                    {
                        lastVerificationError="حساب مدير الكرت لا يطابق حساب المدير المحدد.";
                        break;
                    }
                    if(string.Equals(Get(actual,"actual-profile"),voucher.Profile,
                       StringComparison.OrdinalIgnoreCase))
                        return new UserManagerV6DeliveryResult(true,false,id,null);
                    lastVerificationError="حساب الكرت موجود لكن باقته لم تتأكد في User Manager.";
                }
                return new UserManagerV6DeliveryResult(false,true,id,
                    (lastVerificationError??"تعذر تأكيد ظهور الكرت وباقته.")+
                    " راجع الكرت بالاسم في User Manager قبل الطباعة، ولا تُعد إرساله.");

            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException
                or InvalidDataException or ObjectDisposedException)
            {
                return new UserManagerV6DeliveryResult(false,true,id,
                    "استجاب الراوتر لأمر إنشاء الحساب وربط الباقة، لكن تعذر تأكيد ظهوره: "+
                    ex.Message+" | لا تُعد إصدار الكرت أو طباعته قبل التحقق.");
            }
        }
        catch (RouterOsTrapException ex)
        {
            // Definitive rejection: attempt to roll back only the newly created ID.
            try
            {
                await client.CommandAsync("/tool/user-manager/user/remove",
                    new Dictionary<string,string> { [".id"] = id }, ct);
                return new UserManagerV6DeliveryResult(false, false, id,
                    "رفض الراوتر الباقة وأُلغي الحساب الجديد: " + ex.Message);
            }
            catch (Exception rollbackError) when (rollbackError is IOException or OperationCanceledException)
            {
                return new UserManagerV6DeliveryResult(false, true, id,
                    "رفضت الباقة، ولم نتأكد من إلغاء الحساب الجديد؛ راجعه على الراوتر.");
            }
        }
        catch (IOException)
        {
            return new UserManagerV6DeliveryResult(false, true, id,
                "ربط الباقة غير مؤكد بعد انقطاع الاتصال؛ لم تُحذف بيانات العميل أو تُعَد العملية.");
        }
        catch (OperationCanceledException)
        {
            return new UserManagerV6DeliveryResult(false, true, id,
                "أُلغي انتظار ربط الباقة؛ قد يكون الراوتر نفذ الربط بالفعل.");
        }
    }

    /// <summary>Validate the whole batch before any write, and stop at the first ambiguous result.</summary>
    public async Task<BatchDeliveryResult> UploadUserManagerV6BatchAsync(
        IReadOnlyList<Voucher> vouchers, string customer, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(vouchers);
        if (vouchers.Count == 0 || vouchers.Count > 10000)
            throw new ArgumentException("عدد كروت الدفعة غير صالح.", nameof(vouchers));
        if (string.IsNullOrWhiteSpace(customer))
            throw new ArgumentException("اختر حساب مدير User Manager.", nameof(customer));
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var card in vouchers)
        {
            if (card is null || !names.Add(card.Username))
                throw new ArgumentException("توجد أسماء كروت مكررة أو بيانات غير صالحة.");
            if (string.IsNullOrWhiteSpace(card.Profile)
                || card.Password is null || card.Password.Length>128)
                throw new ArgumentException("توجد كروت دون باقة أو ببيانات مرور غير صالحة.");
        }

        int succeeded = 0;
        var failed = new List<string>();
        foreach (var card in vouchers)
        {
            ct.ThrowIfCancellationRequested();
            var status = await CreateAndActivateUserManagerV6Async(card, customer, ct);
            if (status.Success) { succeeded++; continue; }
            if (status.UncertainOutcome)
                return new BatchDeliveryResult(succeeded, failed, true);
            failed.Add(card.Username);
        }
        return new BatchDeliveryResult(succeeded, failed, false);
    }

    public async Task AddHotspotUserAsync(Voucher voucher, CancellationToken ct = default)
    {
        if (voucher.Username.Length is < 1 or > 64 ||
            voucher.Password is null || voucher.Password.Length > 128)
            throw new ArgumentException("بيانات الكرت غير صالحة.");
        // Hadramout's username-only Hotspot mode omits the password property.
        // Sending a matching password here would silently change the login mode.
        var args = new Dictionary<string, string>
        {
            ["name"] = voucher.Username,
            ["profile"] = voucher.Profile,
            ["comment"] = "NajmCard batch " + voucher.BatchId
        };
        if (voucher.Password.Length > 0)
            args["password"] = voucher.Password;
        await client.CommandAsync("/ip/hotspot/user/add", args, ct);
    }

    public async Task<BatchDeliveryResult> UploadBatchAsync(IReadOnlyList<Voucher> vouchers, CancellationToken ct = default)
    {
        if (vouchers.Count == 0) throw new ArgumentException("لا توجد كروت لإرسالها.");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var voucher in vouchers)
            if (!seen.Add(voucher.Username)) throw new ArgumentException("اسم كرت مكرر في الدفعة.");
        var existing = (await ReadUsersAsync(ct)).Select(u => u.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var collisions = vouchers.Where(v => existing.Contains(v.Username)).Select(v => v.Username).ToList();
        if (collisions.Count != 0) throw new ArgumentException("أرقام الكروت موجودة مسبقاً على الراوتر؛ لم يُرسل أي كرت.");
        var failed = new List<string>();
        int sent = 0;
        foreach (var voucher in vouchers)
        {
            try { await AddHotspotUserAsync(voucher, ct); sent++; }
            catch (RouterOsTrapException) { failed.Add(voucher.Username); }
            catch (IOException)
            {
                // Router may have accepted the last request before network failure; do not retry blindly.
                return new BatchDeliveryResult(sent, failed, true);
            }
            catch (OperationCanceledException) { throw; }
        }
        return new BatchDeliveryResult(sent, failed, false);
    }
}
