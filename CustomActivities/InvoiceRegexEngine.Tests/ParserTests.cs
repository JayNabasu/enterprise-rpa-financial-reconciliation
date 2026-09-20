using EnterpriseRPA.RegexEngine;
using Xunit;

namespace EnterpriseRPA.RegexEngine.Tests
{
    public class InvoiceRegexParserTests
    {
        [Fact]
        public void Parse_StandardEnergyVendorInvoice_ExtractsAllFieldsAccurately()
        {
            string sampleInvoice = @"
                DELTA OILFIELD SERVICES LIMITED
                Plot 14 Trans-Amadi Industrial Layout, Port Harcourt, Nigeria
                TIN: 10482910-0001
                
                COMMERCIAL INVOICE
                Invoice Number: INV-2024-8841
                Invoice Date: 15-Oct-2024
                Purchase Order No: 4500982314
                Cost Center: CC-OPS-OML119
                
                Description: Offshore Wellhead Inspection & Preventative Maintenance Services
                Sub Total: 120,000.00 USD
                VAT (7.5%): 9,000.00 USD
                WHT: 6,000.00 USD
                Total Gross: 129,000.00 USD
            ";

            var result = InvoiceRegexParser.Parse(sampleInvoice);

            Assert.Equal("INV-2024-8841", result.InvoiceNumber);
            Assert.Equal("4500982314", result.PurchaseOrderNumber);
            Assert.Equal("10482910-0001", result.TaxIdNumber);
            Assert.Equal("USD", result.Currency);
            Assert.Equal(120000.00m, result.NetAmount);
            Assert.Equal(9000.00m, result.VatAmount);
            Assert.Equal(6000.00m, result.WhtAmount);
            Assert.Equal(129000.00m, result.GrossAmount);
            Assert.True(result.IsMathematicallyValid);
            Assert.Contains("CC-OPS-OML119", result.ExtractedCostCenters);
            Assert.True(result.ConfidenceScore >= 0.85);
        }

        [Fact]
        public void Parse_NairaDomesticInvoice_HandlesCurrencyAndMathVerification()
        {
            string sampleInvoice = @"
                ATLANTIC LOGISTICS & MARINE LTD
                Victoria Island, Lagos
                FIRS TIN: 2289410941
                
                Invoice Ref: ATL-NG-9021
                Date: 2024-11-04
                PO Ref: 4500114920
                CostCenter: CC-LOG-NEPL
                
                Net Amount: ₦ 45,000,000.00
                VAT Amount: ₦ 3,375,000.00
                Grand Total: ₦ 48,375,000.00
            ";

            var result = InvoiceRegexParser.Parse(sampleInvoice);

            Assert.Equal("ATL-NG-9021", result.InvoiceNumber);
            Assert.Equal("4500114920", result.PurchaseOrderNumber);
            Assert.Equal("2289410941", result.TaxIdNumber);
            Assert.Equal("NGN", result.Currency);
            Assert.Equal(45000000.00m, result.NetAmount);
            Assert.Equal(3375000.00m, result.VatAmount);
            Assert.Equal(48375000.00m, result.GrossAmount);
            Assert.True(result.IsMathematicallyValid);
            Assert.Contains("CC-LOG-NEPL", result.ExtractedCostCenters);
        }

        [Fact]
        public void Parse_DiscrepancyInvoice_FlagsMathematicalInconsistency()
        {
            string badInvoice = @"
                SUPPLIER XYZ
                Invoice #: INV-ERR-001
                P.O. Number: 4500112233
                Net Amount: 100,000.00
                VAT Amount: 7,500.00
                Total Gross: 150,000.00
            ";

            var result = InvoiceRegexParser.Parse(badInvoice);

            Assert.False(result.IsMathematicallyValid);
            Assert.Contains("Variance detected", result.ValidationMessage);
        }
    }
}
