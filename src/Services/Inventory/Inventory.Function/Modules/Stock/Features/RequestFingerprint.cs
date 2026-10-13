using System.Security.Cryptography;
using System.Text;

namespace Inventory.Function.Modules.Stock.Features;

// A stable hash of a movement request, stored on its ledger rows: a replay with the same
// idempotencyKey must carry the same request, or it is IdempotencyKeyReused (ADR-0052 §7).
public static class RequestFingerprint
{
    public static string Of(params object?[] parts)
    {
        var canonical = string.Join('\u001f', parts.Select(part => part switch
        {
            // Same normalisation as StockBalance: blank and absent are the same value.
            null => "␀",
            string text when string.IsNullOrWhiteSpace(text) => "␀",
            string text => text.Trim(),
            IFormattable value => value.ToString(null, CultureInfo.InvariantCulture),
            _ => part.ToString(),
        }));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
