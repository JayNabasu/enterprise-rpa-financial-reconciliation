using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace EnterpriseRPA.RegexEngine
{
    public class ExtractedInvoiceData
    {
        public string VendorName { get; set; } = string.Empty;
        public string TaxIdNumber { get; set; } = string.Empty;
        public string InvoiceNumber { get; set; } = string.Empty;
        public string PurchaseOrderNumber { get; set; } = string.Empty;
        public string InvoiceDate { get; set; } = string.Empty;
        public string Currency { get; set; } = "USD";
        public decimal NetAmount { get; set; }
        public decimal VatAmount { get; set; }
        public decimal WhtAmount { get; set; }
        public decimal GrossAmount { get; set; }
        public double ConfidenceScore { get; set; }
        public bool IsMathematicallyValid { get; set; }
        public string ValidationMessage { get; set; } = string.Empty;
        public List<string> ExtractedCostCenters { get; set; } = new();
    }

    public static partial class InvoiceRegexParser
    {
        // Precompiled regex patterns for maximum throughput and enterprise reliability
        private static readonly Regex InvoiceNoRegex = new(
            @"(?:Invoice\s*(?:No|Number|#|Ref)[\s.:#]*)\s*([A-Z0-9\-_]{4,25})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex PurchaseOrderRegex = new(
            @"(?:(?:Purchase\s*Order|P\.?O\.?)\s*(?:No|Number|#|Ref)?[\s.:#]*)\s*(45\d{8}|[A-Z0-9\-_]{5,20})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex TinRegex = new(
            @"(?:(?:TIN|Tax\s*ID|VAT\s*Reg|FIRS\s*TIN)[\s.:#]*)\s*([0-9]{8,14}(?:-[0-9]{4})?)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex DateRegex = new(
            @"(?:Date|Invoice\s*Date)[\s.:#]*\s*(\d{1,2}[-/.](?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec|\d{1,2})[-/.]\d{2,4}|\d{4}[-/.]\d{2}[-/.]\d{2})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex CurrencyRegex = new(
            @"(?:\b(USD|NGN|EUR|GBP)\b|([$₦€£]))",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex NetAmountRegex = new(
            @"(?:Sub\s*Total|Net\s*Amount|Taxable\s*Amount)[\s.:$₦€£]*([\d,]+\.\d{2})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex VatRegex = new(
            @"(?:VAT\s*(?:\(7\.5%\)|7\.5%|Amount)?|Value\s*Added\s*Tax)[\s.:$₦€£]*([\d,]+\.\d{2})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex WhtRegex = new(
            @"(?:WHT|Withholding\s*Tax(?:\s*(?:5%|10%))?)[\s.:$₦€£]*([\d,]+\.\d{2})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex TotalGrossRegex = new(
            @"(?:Total\s*Gross|Invoice\s*Total|Total\s*Payable|Amount\s*Due|Grand\s*Total)[\s.:$₦€£]*([\d,]+\.\d{2})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex CostCenterRegex = new(
            @"(?:CC|CostCenter|Cost\s*Center|WBS)[\s.:#]*([A-Z0-9\-_]{4,25})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static ExtractedInvoiceData Parse(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
            {
                return new ExtractedInvoiceData { ConfidenceScore = 0.0, ValidationMessage = "Raw text is empty" };
            }

            var result = new ExtractedInvoiceData();
            int matchedFields = 0;
            const int totalExpectedFields = 7;

            // 1. Invoice Number
            var invMatch = InvoiceNoRegex.Match(rawText);
            if (invMatch.Success)
            {
                result.InvoiceNumber = invMatch.Groups[1].Value.Trim();
                matchedFields++;
            }

            // 2. PO Number (standard SAP 4500###### or customized PO)
            var poMatch = PurchaseOrderRegex.Match(rawText);
            if (poMatch.Success)
            {
                result.PurchaseOrderNumber = poMatch.Groups[1].Value.Trim();
                matchedFields++;
            }

            // 3. Tax ID (TIN)
            var tinMatch = TinRegex.Match(rawText);
            if (tinMatch.Success)
            {
                result.TaxIdNumber = tinMatch.Groups[1].Value.Trim();
                matchedFields++;
            }

            // 4. Invoice Date
            var dateMatch = DateRegex.Match(rawText);
            if (dateMatch.Success)
            {
                result.InvoiceDate = dateMatch.Groups[1].Value.Trim();
                matchedFields++;
            }

            // 5. Currency
            var currMatch = CurrencyRegex.Match(rawText);
            if (currMatch.Success)
            {
                string rawCurr = currMatch.Value.Trim().ToUpperInvariant();
                result.Currency = rawCurr switch
                {
                    "$" => "USD",
                    "₦" => "NGN",
                    "€" => "EUR",
                    "£" => "GBP",
                    _ => rawCurr
                };
            }

            // 6. Financial Amounts
            var netMatch = NetAmountRegex.Match(rawText);
            if (netMatch.Success && TryParseDecimal(netMatch.Groups[1].Value, out decimal net))
            {
                result.NetAmount = net;
                matchedFields++;
            }

            var vatMatch = VatRegex.Match(rawText);
            if (vatMatch.Success && TryParseDecimal(vatMatch.Groups[1].Value, out decimal vat))
            {
                result.VatAmount = vat;
                matchedFields++;
            }

            var whtMatch = WhtRegex.Match(rawText);
            if (whtMatch.Success && TryParseDecimal(whtMatch.Groups[1].Value, out decimal wht))
            {
                result.WhtAmount = wht;
            }

            var grossMatch = TotalGrossRegex.Match(rawText);
            if (grossMatch.Success && TryParseDecimal(grossMatch.Groups[1].Value, out decimal gross))
            {
                result.GrossAmount = gross;
                matchedFields++;
            }

            // 7. Cost Centers / WBS
            var ccMatches = CostCenterRegex.Matches(rawText);
            foreach (Match m in ccMatches)
            {
                string cc = m.Groups[1].Value.Trim();
                if (!result.ExtractedCostCenters.Contains(cc))
                {
                    result.ExtractedCostCenters.Add(cc);
                }
            }

            // Mathematical Consistency & Reconciliation Validation
            ValidateMath(result);

            // Confidence scoring
            result.ConfidenceScore = Math.Round((double)matchedFields / totalExpectedFields, 2);

            return result;
        }

        private static void ValidateMath(ExtractedInvoiceData data)
        {
            if (data.GrossAmount <= 0)
            {
                data.IsMathematicallyValid = false;
                data.ValidationMessage = "Gross amount must be greater than zero.";
                return;
            }

            // Expected gross formula: Net + VAT (or Net + VAT - WHT depending on payment terms)
            decimal expectedGross = data.NetAmount + data.VatAmount;
            decimal tolerance = 0.05m; // 5 cents/kobo rounding tolerance

            if (data.NetAmount > 0 && Math.Abs(expectedGross - data.GrossAmount) <= tolerance)
            {
                data.IsMathematicallyValid = true;
                data.ValidationMessage = "Mathematical reconciliation verified: Net + VAT equals Gross.";
            }
            else if (data.NetAmount > 0 && Math.Abs((data.NetAmount + data.VatAmount - data.WhtAmount) - data.GrossAmount) <= tolerance)
            {
                data.IsMathematicallyValid = true;
                data.ValidationMessage = "Mathematical reconciliation verified: Net + VAT - WHT equals Gross.";
            }
            else if (data.NetAmount == 0 && data.GrossAmount > 0)
            {
                // In some documents only Gross and VAT are specified
                data.NetAmount = data.GrossAmount - data.VatAmount;
                data.IsMathematicallyValid = true;
                data.ValidationMessage = "Calculated Net from Gross and VAT.";
            }
            else
            {
                data.IsMathematicallyValid = false;
                data.ValidationMessage = $"Variance detected: Net ({data.NetAmount}) + VAT ({data.VatAmount}) != Gross ({data.GrossAmount})";
            }
        }

        private static bool TryParseDecimal(string input, out decimal value)
        {
            string sanitized = input.Replace(",", "").Trim();
            return decimal.TryParse(sanitized, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
        }
    }
}
