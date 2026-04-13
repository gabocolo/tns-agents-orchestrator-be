using Domain.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace Infrastructure.Ai
{
    public class RegexDlpFilter : IDlpFilter
    {
        private readonly ILogger<RegexDlpFilter> _logger;

        // Patrones PII ordenados de más específico a más general
        private static readonly (string Name, Regex Pattern, string Placeholder)[] PiiPatterns =
        {
            ("CreditCard",
                new Regex(@"\b(?:4[0-9]{12}(?:[0-9]{3})?|5[1-5][0-9]{14}|3[47][0-9]{13}|6(?:011|5[0-9]{2})[0-9]{12})\b",
                    RegexOptions.Compiled),
                "[CREDIT_CARD_REDACTED]"),

            ("SSN",
                new Regex(@"\b\d{3}-\d{2}-\d{4}\b",
                    RegexOptions.Compiled),
                "[SSN_REDACTED]"),

            ("Email",
                new Regex(@"\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\b",
                    RegexOptions.Compiled),
                "[EMAIL_REDACTED]"),

            ("PhoneInternational",
                new Regex(@"\+\d{1,3}[\s\-]?\(?\d{1,4}\)?[\s\-]?\d{3,4}[\s\-]?\d{3,4}\b",
                    RegexOptions.Compiled),
                "[PHONE_REDACTED]"),

            ("PhoneUS",
                new Regex(@"\b\(?\d{3}\)?[\s\-.]?\d{3}[\s\-.]?\d{4}\b",
                    RegexOptions.Compiled),
                "[PHONE_REDACTED]"),

            ("IPv4",
                new Regex(@"\b(?:(?:25[0-5]|2[0-4]\d|[01]?\d\d?)\.){3}(?:25[0-5]|2[0-4]\d|[01]?\d\d?)\b",
                    RegexOptions.Compiled),
                "[IP_REDACTED]"),
        };

        public RegexDlpFilter(ILogger<RegexDlpFilter> logger)
        {
            _logger = logger;
        }

        public Task<string> SanitizeInputAsync(string text, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(text))
                return Task.FromResult(text);

            var sanitized = text;
            var totalReplacements = 0;

            foreach (var (name, pattern, placeholder) in PiiPatterns)
            {
                var matches = pattern.Matches(sanitized);
                if (matches.Count > 0)
                {
                    totalReplacements += matches.Count;
                    sanitized = pattern.Replace(sanitized, placeholder);
                }
            }

            if (totalReplacements > 0)
            {
                _logger.LogWarning(
                    "[RegexDlpFilter] Sanitizado input: {Count} coincidencias PII reemplazadas.",
                    totalReplacements);
            }

            return Task.FromResult(sanitized);
        }

        public Task<DlpResult> ValidateOutputAsync(string text, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return Task.FromResult(new DlpResult
                {
                    SanitizedText = text,
                    Findings = new List<string>()
                });
            }

            var sanitized = text;
            var findings = new List<string>();

            foreach (var (name, pattern, placeholder) in PiiPatterns)
            {
                var matches = pattern.Matches(sanitized);
                if (matches.Count > 0)
                {
                    findings.Add($"{name}: {matches.Count} ocurrencia(s) detectada(s)");
                    sanitized = pattern.Replace(sanitized, placeholder);
                }
            }

            if (findings.Count > 0)
            {
                _logger.LogWarning(
                    "[RegexDlpFilter] Validación output: {FindingCount} tipos de PII detectados. Detalles: {Findings}",
                    findings.Count, string.Join("; ", findings));
            }

            return Task.FromResult(new DlpResult
            {
                SanitizedText = sanitized,
                Findings = findings
            });
        }
    }
}
