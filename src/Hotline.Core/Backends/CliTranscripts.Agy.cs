using System.Text;
using Hotline.Core.Chat;
using Microsoft.Data.Sqlite;

namespace Hotline.Core.Backends;

public static partial class CliTranscripts
{
    // ---- agy: ~/.gemini/antigravity-cli/conversations/<id>.db ---------------------------------------------------

    private const int AgyUserStep = 14, AgyAnswerStep = 15;

    /// <summary>
    /// agy keeps each step as a protobuf message in SQLite (steps.step_payload). Read without its schema: a user step
    /// (type 14) holds the text in field 19 › 2, an answer (type 15) in field 20 › 1. Anything else is skipped.
    /// </summary>
    private static List<HistoryStore.Entry>? ReadAgy(string file, string backendId)
    {
        if (!File.Exists(file)) return null;
        var at = new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero);
        var entries = new List<HistoryStore.Entry>();
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 1 }.ToString());
        db.Open();
        using var query = db.CreateCommand();
        query.CommandText = "SELECT step_type, step_payload FROM steps ORDER BY idx";
        using var reader = query.ExecuteReader();
        while (reader.Read())
        {
            if (reader.GetValue(0) is not long stepType || reader.GetValue(1) is not byte[] payload) continue; // NULL or odd values
            string? text;
            try
            {
                text = stepType switch
                {
                    AgyUserStep => Proto(payload, 19) is { } user ? ProtoString(user, 2) : null,
                    AgyAnswerStep => Proto(payload, 20) is { } answer ? ProtoString(answer, 1) : null,
                    _ => null,
                };
            }
            catch (Exception ex) when (ex is IndexOutOfRangeException or FormatException) { continue; } // a malformed step: skip it
            if (text is not null) Add(entries, stepType == AgyUserStep ? ChatRole.User : ChatRole.Assistant, text, at, backendId);
        }
        return entries;
    }

    /// <summary>The first length-delimited field <paramref name="number"/> of a protobuf message, or null.</summary>
    private static byte[]? Proto(byte[] message, int number)
    {
        var i = 0;
        while (i < message.Length)
        {
            var key = Varint(message, ref i);
            var (field, wire) = ((int)(key >> 3), (int)(key & 7));
            switch (wire)
            {
                case 0: Varint(message, ref i); break;
                case 1: if (message.Length - i < 8) return null; i += 8; break;
                case 5: if (message.Length - i < 4) return null; i += 4; break;
                case 2:
                    var length = Varint(message, ref i);
                    if (length > (ulong)(message.Length - i)) return null; // past the end (compared without int overflow)
                    if (field == number) return message[i..(i + (int)length)];
                    i += (int)length;
                    break;
                default: return null; // groups or garbage: not a message this reader understands
            }
        }
        return null;
    }

    private static string? ProtoString(byte[] message, int number) => Proto(message, number) is { } bytes ? Encoding.UTF8.GetString(bytes) : null;

    private static ulong Varint(byte[] b, ref int i)
    {
        ulong value = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            var x = b[i++]; // past the end: IndexOutOfRangeException, caught by Read
            value |= (ulong)(x & 0x7F) << shift;
            if (x < 0x80) return value;
        }
        throw new FormatException("varint too long");
    }
}
