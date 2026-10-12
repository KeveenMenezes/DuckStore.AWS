namespace Payment.Function.Modules.Payments.Domain.Services;

// Luhn checksum — the same rule FluentValidation's CreditCard() applied. Spaces and dashes are
// accepted as separators.
public static class Luhn
{
    public static bool IsValid(string? cardNumber)
    {
        if (string.IsNullOrWhiteSpace(cardNumber))
        {
            return false;
        }

        var sum = 0;
        var even = false;

        for (var i = cardNumber.Length - 1; i >= 0; i--)
        {
            var c = cardNumber[i];
            if (c is ' ' or '-')
            {
                continue;
            }

            if (!char.IsAsciiDigit(c))
            {
                return false;
            }

            var digit = c - '0';
            if (even)
            {
                digit *= 2;
                if (digit > 9)
                {
                    digit -= 9;
                }
            }

            sum += digit;
            even = !even;
        }

        return sum % 10 == 0;
    }
}
