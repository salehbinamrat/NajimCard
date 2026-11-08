using System.Numerics;
using System.Security.Cryptography;
using NajmCard.Core.Models;

namespace NajmCard.Core.Services;

/// <summary>Pure cryptographically random voucher generation, with bounded collision retries.</summary>
public static class VoucherGenerator
{
    public const string Digits = "0123456789";
    public const string UppercaseLetters = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    public const string Mixed = Digits + UppercaseLetters;

    public static IReadOnlyList<Voucher> Generate(VoucherGenerationOptions options,
        IReadOnlySet<string>? reserved = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        if(options.Count is < 1 or > 10000)
            throw new ArgumentOutOfRangeException(nameof(options.Count));
        if(options.Length is < 4 or > 32)
            throw new ArgumentOutOfRangeException(nameof(options.Length));
        var prefix=options.Prefix ?? "";
        if(prefix.Length > 24 || prefix.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '-')))
            throw new ArgumentException("بادئة الكرت يجب أن تتكون من أحرف إنجليزية وأرقام أو - و _.");
        if(string.IsNullOrWhiteSpace(options.Profile))
            throw new ArgumentException("اختر الباقة أولاً.");
        string chars = Alphabet(options.Characters,"أحرف اسم المستخدم",minimum:2);
        var suffixLength=options.LengthIncludesPrefix ? options.Length-prefix.Length : options.Length;
        if(suffixLength < 0)
            throw new ArgumentException("بادئة الكرت أطول من طول الرقم المطلوب.");
        if(options.LengthIncludesPrefix && suffixLength == 0 && options.Count != 1)
            throw new ArgumentException("البادئة تشغل رقم الكرت كاملا؛ لا يمكن إنشاء عدة كروت مختلفة.");
        int passwordLength=options.PasswordLength==0 && options.PasswordCharacters is null
            ? options.Length : options.PasswordLength;
        string passwordChars=options.PasswordCharacters is null ? chars
            : Alphabet(options.PasswordCharacters,"أحرف كلمة المرور",minimum:1);
        if(!options.PasswordEqualsUsername &&
            passwordLength is < 1 or > 64)
            throw new ArgumentException("طول كلمة المرور يجب أن يكون من 1 إلى 64.");
        var possibilities=BigInteger.Pow(chars.Length,suffixLength);
        if(possibilities < options.Count)
            throw new ArgumentException(
                "مساحة أرقام الكروت لا تكفي للعدد المطلوب؛ زد الطول أو مجموعة الأحرف.");
        var used = new HashSet<string>(
            reserved ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);
        var results=new List<Voucher>(options.Count);
        string batch=DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss")+"-"+
            Guid.NewGuid().ToString("N")[..8];
        long budget=Math.Max((long)options.Count*65,200);
        while(results.Count < options.Count && budget-- > 0)
        {
            string username=prefix+NextToken(chars,suffixLength);
            if(!used.Add(username))continue;
            string password=options.PasswordEqualsUsername ? username
                : DifferentPassword(passwordChars,passwordLength,username);
            results.Add(new Voucher(username,password,options.Profile,batch,DateTimeOffset.UtcNow));
        }
        if(results.Count != options.Count)
            throw new InvalidOperationException(
                "تعذر توليد عدد الكروت المطلوبة دون تكرار؛ غيّر الأحرف أو طول الرقم.");
        return results;
    }

    private static string Alphabet(string? input,string label,int minimum)
    {
        string chars=new((input??"").Distinct().ToArray());
        if(chars.Length<minimum || chars.Any(c=>!char.IsAsciiLetterOrDigit(c)))
            throw new ArgumentException(label+" يجب أن تكون أحرفا إنجليزية أو أرقاما فقط.");
        return chars;
    }

    private static string DifferentPassword(string chars,int length,string username)
    {
        for(var attempt=0;attempt<16;attempt++)
        {
            var password=NextToken(chars,length);
            if(!string.Equals(password,username,StringComparison.Ordinal))
                return password;
        }
        throw new ArgumentException(
            "إعدادات كلمة المرور تكرر اسم المستخدم ولا تسمح بإنشاء كلمة مختلفة.");
    }

    private static string NextToken(string chars,int length)
    {
        Span<char> result=length<=64?stackalloc char[length]:new char[length];
        for(int i=0;i<length;++i)
            result[i]=chars[RandomNumberGenerator.GetInt32(chars.Length)];
        return new string(result);
    }
}
