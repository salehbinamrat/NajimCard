namespace NajmCard.Core.Services;

/// <summary>Full RouterOS API result. A successful /add may return the new .id only in !done =ret=.</summary>
public sealed record RouterCommandReply(
    IReadOnlyList<IReadOnlyDictionary<string, string>> Records,
    IReadOnlyDictionary<string, string> Done);

/// <summary>
/// Parse the exact raw sentence contract from RouterOS 6 (read via RouterOsProtocol).
/// Never infer a new row identifier from a displayed user name or an empty !done.
/// </summary>
public static class RouterOsReplyDecoder
{
    private static IReadOnlyDictionary<string,string> Attributes(IReadOnlyList<string> sentence)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var word in sentence.Skip(1))
        {
            if (!word.StartsWith('=')) continue;
            int split = word.IndexOf('=', 1);
            if (split > 1) result[word[1..split]] = word[(split + 1)..];
        }
        return result;
    }

    public static RouterCommandReply Decode(IReadOnlyList<IReadOnlyList<string>> sentences)
    {
        ArgumentNullException.ThrowIfNull(sentences);
        var records = new List<IReadOnlyDictionary<string,string>>();
        IReadOnlyDictionary<string,string>? done = null;
        foreach (var sentence in sentences)
        {
            if (sentence.Count == 0)
                throw new InvalidDataException("RouterOS sent an empty API sentence");
            switch (sentence[0])
            {
                case "!re":
                    if (done is not null)
                        throw new InvalidDataException("RouterOS records received after !done");
                    records.Add(Attributes(sentence));
                    break;
                case "!done":
                    if (done is not null)
                        throw new InvalidDataException("Duplicate RouterOS !done response");
                    done = Attributes(sentence);
                    break;
                default:
                    throw new InvalidDataException("Unexpected RouterOS API response sentence");
            }
        }
        if (done is null)
            throw new InvalidDataException("RouterOS command did not return !done");
        return new RouterCommandReply(records, done);
    }
}
