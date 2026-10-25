using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.IO;
using System.Text;
using System.Text.Json;

namespace BrokerCalculator
{
    public class BondInfo
    {
        public string id { get; set; } = "";
        public string bondNumber { get; set; } = "";
        public double couponRate { get; set; }
        public string auctionDate { get; set; } = "";
        public string maturityDate { get; set; } = "";
        public string couponDate1 { get; set; } = "";
        public string couponDate2 { get; set; } = "";
        public int tenor { get; set; }
        public string lastCoupon { get; set; } = "";
        public string nextCoupon { get; set; } = "";
        public string lastCouponDisplay { get; set; } = "";
        public string nextCouponDisplay { get; set; } = "";
    }

    public partial class MainWindow : Window
    {
        private DispatcherTimer _typingTimer;
        private DispatcherTimer _toastTimer;
        private bool _isCalculatingSharesMode;
        private bool _isInitialized = false;
        private List<ChargeItem> _currentChargesToReport = new List<ChargeItem>();
        private List<BondInfo> _allBonds = new List<BondInfo>();
        private bool _isSecondaryBond => (MarketBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() == "Bond - Secondary Market";
        private double _lastCleanPrice = 0;
        private double _lastDirtyPrice = 0;
        private double _lastAccruedInterest = 0;
        private double _lastCost = 0;
        private BondInfo _currentBond = null;
        private DateTime _lastCouponDate;
        private DateTime _nextCouponDate;

        public MainWindow()
        {
            InitializeComponent();
            _typingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _typingTimer.Tick += TypingTimer_Tick;
            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _toastTimer.Tick += ToastTimer_Tick;
            if (PriceBox != null) PriceBox.Text = "0";
            if (SharesBox != null) SharesBox.Text = "0";
            if (TotalAmountBox != null) TotalAmountBox.Text = "0";
            if (FaceValueBox != null) FaceValueBox.Text = "10000000";
            if (BondPriceBox != null) BondPriceBox.Text = "100";
            if (BondYieldBox != null) BondYieldBox.Text = "10";
            if (SettlementDatePicker != null) SettlementDatePicker.SelectedDate = DateTime.Today.AddDays(2);
            LoadBonds();
            _isCalculatingSharesMode = false;
            _isInitialized = true;
            UpdateInputVisibility();
            CalculateBrokerage();
        }

        private void LoadBonds()
        {
            try
            {
                string[] possiblePaths = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bonds.json"),
                    Path.Combine(Directory.GetCurrentDirectory(), "bonds.json"),
                    "bonds.json",
                    Path.Combine("..", "..", "..", "bonds.json"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bond.json"),
                    Path.Combine(Directory.GetCurrentDirectory(), "bond.json"),
                    "bond.json",
                    Path.Combine("..", "..", "..", "bond.json")
                };
                string foundPath = possiblePaths.FirstOrDefault(File.Exists);
                if (foundPath != null)
                {
                    var json = File.ReadAllText(foundPath);
                    var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    _allBonds = JsonSerializer.Deserialize<List<BondInfo>>(json, opts) ?? new List<BondInfo>();
                    if (BondFetchStatus != null) BondFetchStatus.Text = $"{_allBonds.Count} bonds loaded offline";
                }
                else
                {
                    if (BondFetchStatus != null) BondFetchStatus.Text = "bonds.json not found";
                }
            }
            catch (Exception ex)
            {
                if (BondFetchStatus != null) BondFetchStatus.Text = $"Load error: {ex.Message}";
            }
        }

        private void BondFetch_Click(object sender, RoutedEventArgs e)
        {
            if (BondNumberBox == null) return;
            var num = BondNumberBox.Text.Trim();
            if (string.IsNullOrEmpty(num))
            {
                if (BondFetchStatus != null) BondFetchStatus.Text = "Enter bond number e.g. 698";
                return;
            }

            DateTime settlement = SettlementDatePicker?.SelectedDate ?? DateTime.Today;
            var matches = _allBonds
                .Where(b =>
                    b.bondNumber.Trim() == num ||
                    b.bondNumber.Trim().TrimStart('0') == num.TrimStart('0'))
                .OrderBy(b => Math.Abs((ParseBondDate(b.auctionDate) - settlement).TotalDays))
                .ToList();

            if (matches.Count == 0)
            {
                if (BondFetchStatus != null) BondFetchStatus.Text = $"Bond {num} not found offline";
                return;
            }

            _currentBond = matches.First();
            if (CouponRateText != null) CouponRateText.Text = $"{_currentBond.couponRate}%";
            if (MaturityDateText != null) MaturityDateText.Text = ParseBondDate(_currentBond.maturityDate).ToString("yyyy-MM-dd");
            if (AuctionDateText != null) AuctionDateText.Text = ParseBondDate(_currentBond.auctionDate).ToString("yyyy-MM-dd");
            if (BondFetchStatus != null)
            {
                BondFetchStatus.Text = $"✓ Found bond {_currentBond.bondNumber} (auction {ParseBondDate(_currentBond.auctionDate).ToString("yyyy-MM-dd")})";
            }

            try
            {
                if (!string.IsNullOrEmpty(_currentBond.lastCouponDisplay) && ValLastCouponText != null) ValLastCouponText.Text = _currentBond.lastCouponDisplay;
                if (!string.IsNullOrEmpty(_currentBond.nextCouponDisplay) && ValNextCouponText != null) ValNextCouponText.Text = _currentBond.nextCouponDisplay;
            }
            catch { }

            if (BondYieldBox != null && string.IsNullOrWhiteSpace(BondYieldBox.Text)) BondYieldBox.Text = _currentBond.couponRate.ToString();
        }

        private DateTime ParseBondDate(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return DateTime.Today;
            if (DateTime.TryParse(s, out var dt)) return dt;
            if (s.Length >= 10 && DateTime.TryParseExact(s.Substring(0, 10), "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out dt)) return dt;
            return DateTime.Today;
        }

        private DateTime BuildCouponDate(int year, string monthDay)
        {
            if (string.IsNullOrWhiteSpace(monthDay)) return default;
            var parts = monthDay.Split('-');
            if (parts.Length < 2) return default;
            if (!int.TryParse(parts[0], out int month) || !int.TryParse(parts[1], out int day)) return default;
            if (month < 1 || month > 12) return default;
            int safeDay = Math.Min(day, DateTime.DaysInMonth(year, month));
            return new DateTime(year, month, safeDay);
        }

        private (DateTime lastCoupon, DateTime nextCoupon, List<DateTime> futureCoupons)
            GetCouponSchedule(BondInfo bond, DateTime settlement)
        {
            var maturity = ParseBondDate(bond.maturityDate);
            var issueDate = ParseBondDate(bond.auctionDate);
            var monthDays = new List<string>();
            if (!string.IsNullOrWhiteSpace(bond.couponDate1)) monthDays.Add(bond.couponDate1);
            if (!string.IsNullOrWhiteSpace(bond.couponDate2)) monthDays.Add(bond.couponDate2);

            var candidates = new SortedSet<DateTime>();
            if (monthDays.Count > 0)
            {
                for (int year = issueDate.Year - 1; year <= maturity.Year + 1; year++)
                {
                    foreach (var monthDay in monthDays.Distinct())
                    {
                        var candidate = BuildCouponDate(year, monthDay);
                        if (candidate != default && candidate >= issueDate.AddYears(-1) && candidate <= maturity.AddYears(2))
                            candidates.Add(candidate);
                    }
                }
            }

            if (candidates.Count == 0)
            {
                int matDay = maturity.Day;
                int matMonth = maturity.Month;
                int otherMonth = matMonth - 6;
                if (otherMonth <= 0) otherMonth += 12;
                List<DateTime> all = new List<DateTime>();
                for (int y = settlement.Year - 1; y <= maturity.Year; y++)
                {
                    try { int d = Math.Min(matDay, DateTime.DaysInMonth(y, matMonth)); all.Add(new DateTime(y, matMonth, d)); } catch { }
                    try { int d = Math.Min(matDay, DateTime.DaysInMonth(y, otherMonth)); all.Add(new DateTime(y, otherMonth, d)); } catch { }
                }
                all = all.Distinct().OrderBy(d => d).ToList();
                DateTime last = all.Where(d => d <= settlement).OrderByDescending(d => d).FirstOrDefault();
                if (last == default) last = all.FirstOrDefault();
                DateTime next = all.Where(d => d > settlement).OrderBy(d => d).FirstOrDefault();
                if (next == default) next = maturity;
                List<DateTime> future = all.Where(d => d >= next && d <= maturity).OrderBy(d => d).ToList();
                if (!future.Contains(maturity)) future.Add(maturity);
                future = future.Distinct().OrderBy(d => d).ToList();
                return (last, next, future);
            }

            var ordered = candidates.Where(d => d <= maturity.AddDays(1)).OrderBy(d => d).ToList();
            DateTime last = ordered.Where(d => d <= settlement).OrderByDescending(d => d).FirstOrDefault();
            if (last == default) last = ordered.FirstOrDefault();
            DateTime next = ordered.Where(d => d > settlement).OrderBy(d => d).FirstOrDefault();
            if (next == default) next = maturity;
            var future = ordered.Where(d => d >= next && d <= maturity).OrderBy(d => d).ToList();
            if (future.Count == 0) future.Add(maturity);
            future = future.Distinct().OrderBy(d => d).ToList();
            return (last, next, future);
        }

        private void CalculatePrice_Click(object sender, RoutedEventArgs e)
        {
            if (_currentBond == null)
            {
                MessageBox.Show("Fetch a bond first (e.g. 698)", "No bond", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!double.TryParse(BondYieldBox?.Text, out double yield) || yield <= 0)
            {
                MessageBox.Show("Enter valid Yield (%) e.g. 10", "Invalid Yield", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!double.TryParse(FaceValueBox?.Text, out double fv) || fv <= 0)
            {
                MessageBox.Show("Enter valid Face Value", "Invalid FV", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DateTime settlement = SettlementDatePicker?.SelectedDate ?? DateTime.Today;
            DateTime maturity = ParseBondDate(_currentBond.maturityDate);
            var valuation = CalculateBondValuation(_currentBond, yield, fv, settlement, maturity);
            _lastCleanPrice = valuation.cleanPrice;
            _lastDirtyPrice = valuation.dirtyPrice;
            _lastAccruedInterest = valuation.accruedInterest;
            _lastCost = valuation.cost;
            _lastCouponDate = valuation.lastCoupon;
            _nextCouponDate = valuation.nextCoupon;
            if (ValFaceValueText != null) ValFaceValueText.Text = fv.ToString("N0");
            if (ValYieldText != null) ValYieldText.Text = $"{yield:F4}%";
            if (ValCouponText != null) ValCouponText.Text = $"{_currentBond.couponRate:F2}%";
            if (ValAuctionDateText != null) ValAuctionDateText.Text = ParseBondDate(_currentBond.auctionDate).ToString("yyyy-MM-dd");
            if (ValMaturityDateText != null) ValMaturityDateText.Text = maturity.ToString("yyyy-MM-dd");
            if (ValNextCouponText != null) ValNextCouponText.Text = _nextCouponDate.ToString("dd-MMM-yy");
            if (ValLastCouponText != null) ValLastCouponText.Text = _lastCouponDate.ToString("dd-MMM-yy");
            if (ValSettlementDateText != null) ValSettlementDateText.Text = settlement.ToString("dd-MMM-yy");
            if (ValCleanPriceText != null) ValCleanPriceText.Text = _lastCleanPrice.ToString("F4");
            if (ValAccruedText != null) ValAccruedText.Text = _lastAccruedInterest.ToString("F7");
            if (ValDirtyPriceText != null) ValDirtyPriceText.Text = _lastDirtyPrice.ToString("F5");
            if (ValCostText != null) ValCostText.Text = _lastCost.ToString("N2");
            if (ValBondNumberText != null) ValBondNumberText.Text = _currentBond.bondNumber;
            if (BondPriceBox != null) BondPriceBox.Text = _lastDirtyPrice.ToString("F4");
            CalculateBrokerage();
        }

        private (double cleanPrice, double dirtyPrice, double accruedInterest, double cost, DateTime lastCoupon, DateTime nextCoupon, List<DateTime> futureCoupons)
            CalculateBondValuation(BondInfo bond, double yieldPercent, double faceValue, DateTime settlement, DateTime maturity)
        {
            double couponRate = bond.couponRate;
            double couponSemi = couponRate / 2.0;
            double ySemi = yieldPercent / 2.0 / 100.0;
            var schedule = GetCouponSchedule(bond, settlement);
            DateTime lastCoupon = schedule.lastCoupon;
            DateTime nextCoupon = schedule.nextCoupon;
            List<DateTime> futureCoupons = schedule.futureCoupons;

            int daysSinceLast = (settlement - lastCoupon).Days;
            int daysInPeriod = (nextCoupon - lastCoupon).Days;
            int daysToNext = (nextCoupon - settlement).Days;

            if (daysInPeriod <= 0) daysInPeriod = 182;
            if (daysToNext < 0) daysToNext = 0;

            double accrued = couponSemi * (double)daysSinceLast / (double)daysInPeriod;
            double w = (double)daysToNext / (double)daysInPeriod;
            double dirty = 0.0;

            for (int i = 0; i < futureCoupons.Count; i++)
            {
                double t = w + i;
                double cf = couponSemi;
                if (futureCoupons[i] == maturity) cf += 100.0;
                dirty += cf / Math.Pow(1.0 + ySemi, t);
            }

            double clean = dirty - accrued;
            double cost = dirty * faceValue / 100.0;
            return (clean, dirty, accrued, cost, lastCoupon, nextCoupon, futureCoupons);
        }

        private void SettlementDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e) { }
        public class ChargeItem { public string Name { get; set; } = ""; public string Percent { get; set; } = ""; public string Amount { get; set; } = ""; }
        private void Calculate_Click(object sender, RoutedEventArgs e) { if (!_isInitialized) return; _typingTimer?.Stop(); CalculateBrokerage(); }
        private void MarketBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (!_isInitialized) return; _typingTimer?.Stop(); UpdateInputVisibility(); CalculateBrokerage(); }
        private void Input_TextChanged(object sender, TextChangedEventArgs e) { if (!_isInitialized) return; _typingTimer?.Stop(); _typingTimer?.Start(); }
        private void TypingTimer_Tick(object? sender, EventArgs e) { if (!_isInitialized) return; _typingTimer?.Stop(); CalculateBrokerage(); }
        private void BondType_Checked(object? sender, RoutedEventArgs e) { if (!_isInitialized) return; CalculateBrokerage(); }
        private void TextBox_GotFocus(object sender, RoutedEventArgs e) { if (sender is TextBox tb && tb.Text == "0") tb.Text = ""; }
        private void TextBox_LostFocus(object sender, RoutedEventArgs e) { if (sender is TextBox tb && string.IsNullOrWhiteSpace(tb.Text)) tb.Text = tb.Name == "BondPriceBox" ? "100" : "0"; }
        private void ToastTimer_Tick(object? sender, EventArgs e) { _toastTimer.Stop(); if (ToastBorder != null) ToastBorder.Visibility = Visibility.Collapsed; }
        private void CalcMode_Checked(object? sender, RoutedEventArgs e) { if (!_isInitialized) return; if (sender is RadioButton rb) { _isCalculatingSharesMode = (rb == ModeCalculateShares); UpdateInputVisibility(); } }

        private void UpdateInputVisibility()
        {
            if (!_isInitialized) return;
            bool isSecondary = _isSecondaryBond;
            if (SecondaryBondPanel != null) SecondaryBondPanel.Visibility = isSecondary ? Visibility.Visible : Visibility.Collapsed;
            if (EquityAndPrimaryPanel != null) EquityAndPrimaryPanel.Visibility = isSecondary ? Visibility.Collapsed : Visibility.Visible;
            if (CalcModePanel != null) CalcModePanel.Visibility = isSecondary ? Visibility.Collapsed : Visibility.Visible;
            if (SharesInputPanel != null) SharesInputPanel.Visibility = _isCalculatingSharesMode && !isSecondary ? Visibility.Collapsed : (!isSecondary ? Visibility.Visible : Visibility.Collapsed);
            if (TotalAmountInputPanel != null) TotalAmountInputPanel.Visibility = _isCalculatingSharesMode && !isSecondary ? Visibility.Visible : Visibility.Collapsed;
        }

        private (double brokerage, double vatOnBrokerage, double dse, double dseVat, double cmsa, double fidelity, double csdr, double csdrVat, List<ChargeItem> chargesList, double totalBasePercentage, double totalBaseAmount)
            CalculateChargesFromTurnover(double turnover, string selectedMarket, double vatRate)
        {
            double brokerage = 0, dse = 0, cmsa = 0, fidelity = 0, csdr = 0, vatOnBrokerage = 0, dseVat = 0, csdrVat = 0, effectiveBrokerageRate = 0, totalBasePercentage = 0, totalBaseAmount = 0;
            List<ChargeItem> currentChargesList = new List<ChargeItem>();
            bool isEquity = selectedMarket == "Equity";
            if (isEquity)
            {
                double remaining = turnover;
                double tier1Amount = Math.Min(remaining, 10_000_000.0);
                if (tier1Amount > 0) { brokerage += tier1Amount * 0.017; remaining -= tier1Amount; }
                double tier2Amount = Math.Min(remaining, 40_000_000.0);
                if (tier2Amount > 0) { brokerage += tier2Amount * 0.015; remaining -= tier2Amount; }
                if (remaining > 0) brokerage += remaining * 0.008;
                if (turnover > 0) effectiveBrokerageRate = brokerage / turnover;
                dse = turnover * 0.0014; cmsa = turnover * 0.0014; fidelity = turnover * 0.0002; csdr = turnover * 0.0006;
                vatOnBrokerage = brokerage * vatRate; dseVat = dse * vatRate; csdrVat = csdr * vatRate;
                totalBaseAmount = brokerage + dse + cmsa + fidelity + csdr;
                totalBasePercentage = turnover > 0 ? totalBaseAmount / turnover : 0;
            }
            else
            {
                double bondBrokerageRate = 0.001, bondDSEFeeRate = 0.0005, bondCMSAFeeRate = 0.0003, bondCsdrRate = 0.0002;
                brokerage = turnover * bondBrokerageRate; dse = turnover * bondDSEFeeRate; cmsa = turnover * bondCMSAFeeRate; fidelity = 0; csdr = turnover * bondCsdrRate;
                vatOnBrokerage = brokerage * vatRate; dseVat = dse * vatRate; csdrVat = csdr * vatRate;
                effectiveBrokerageRate = bondBrokerageRate;
                totalBaseAmount = brokerage + dse + cmsa + fidelity + csdr;
                totalBasePercentage = bondBrokerageRate + bondDSEFeeRate + bondCMSAFeeRate + bondCsdrRate;
            }
            string brokeragePercent = isEquity ? (turnover <= 10_000_000.0 ? "1.70%" : turnover <= 50_000_000.0 ? $"{effectiveBrokerageRate * 100:N2}% (1.70% / 1.50%)" : $"{effectiveBrokerageRate * 100:N2}% (1.70% / 1.50% / 0.80%)") : "0.10%";
            string dsePercent = isEquity ? "0.14%" : "0.05%", cmsaPercent = isEquity ? "0.14%" : "0.03%", fidelityPercent = isEquity ? "0.02%" : "0%", csdrPercent = isEquity ? "0.06%" : "0.02%";
            if (brokerage >= 0) currentChargesList.Add(new ChargeItem { Name = isEquity ? "Brokerage" : "Bond Brokerage", Percent = brokeragePercent, Amount = brokerage.ToString("N2") });
            if (vatOnBrokerage > 0) currentChargesList.Add(new ChargeItem { Name = "VAT (on Brokerage)", Percent = "18%", Amount = vatOnBrokerage.ToString("N2") });
            if (dse >= 0) currentChargesList.Add(new ChargeItem { Name = isEquity ? "DSE Fee" : "Bond DSE Fee", Percent = dsePercent, Amount = dse.ToString("N2") });
            if (dseVat > 0) currentChargesList.Add(new ChargeItem { Name = "DSEVAT", Percent = "18%", Amount = dseVat.ToString("N2") });
            if (cmsa >= 0) currentChargesList.Add(new ChargeItem { Name = isEquity ? "CMSA Fee" : "Bond CMSA Fee", Percent = cmsaPercent, Amount = cmsa.ToString("N2") });
            if (fidelity >= 0) currentChargesList.Add(new ChargeItem { Name = "Fidelity Fee", Percent = fidelityPercent, Amount = fidelity.ToString("N2") });
            if (csdr >= 0) currentChargesList.Add(new ChargeItem { Name = "CSDR Fee", Percent = csdrPercent, Amount = csdr.ToString("N2") });
            if (csdrVat > 0) currentChargesList.Add(new ChargeItem { Name = "CSDRVAT", Percent = "18%", Amount = csdrVat.ToString("N2") });
            return (brokerage, vatOnBrokerage, dse, dseVat, cmsa, fidelity, csdr, csdrVat, currentChargesList, totalBasePercentage, totalBaseAmount);
        }

        private (double consideration, double brokerage, double vatBrokerage, double dse, double dseVat, double csdr, double csdrVat, double cmsa, double totalCharges, List<ChargeItem> chargesList)
            CalculateSecondaryBondCharges(double fv, double price, double vatRate)
        {
            double consideration = fv * price / 100.0;
            double brokerage = fv <= 100_000_000.0 ? fv * 0.00063 : (100_000_000.0 * 0.00063) + ((fv - 100_000_000.0) * 0.00035);
            double vatBrokerage = brokerage * vatRate;
            double dse = fv * 0.00017;
            double dseVat = dse * vatRate;
            double csdr = fv * 0.00010;
            double csdrVat = csdr * vatRate;
            double cmsa = consideration * 0.00010;
            double totalCharges = brokerage + vatBrokerage + dse + dseVat + csdr + csdrVat + cmsa;
            var list = new List<ChargeItem>();
            string brokeragePct = fv <= 100_000_000.0 ? "0.063%" : $"{(brokerage / fv * 100):N4}% (0.063%/0.035% Tiered)";
            list.Add(new ChargeItem { Name = "Face Value (FV)", Percent = "-", Amount = fv.ToString("N2") });
            list.Add(new ChargeItem { Name = "Price (Dirty)", Percent = "-", Amount = price.ToString("N4") });
            list.Add(new ChargeItem { Name = "Consideration (FV*Dirty/100)", Percent = "-", Amount = consideration.ToString("N2") });
            if (_lastCleanPrice > 0) list.Add(new ChargeItem { Name = "Clean Price", Percent = "-", Amount = _lastCleanPrice.ToString("N4") });
            if (_lastAccruedInterest > 0) list.Add(new ChargeItem { Name = "Accrued Interest", Percent = "-", Amount = _lastAccruedInterest.ToString("N7") });
            list.Add(new ChargeItem { Name = "Brokerage", Percent = brokeragePct, Amount = brokerage.ToString("N2") });
            list.Add(new ChargeItem { Name = "VAT (on Brokerage)", Percent = "18%", Amount = vatBrokerage.ToString("N2") });
            list.Add(new ChargeItem { Name = "DSE Charges (on FV)", Percent = "0.017%", Amount = dse.ToString("N2") });
            list.Add(new ChargeItem { Name = "DSE VAT", Percent = "18%", Amount = dseVat.ToString("N2") });
            list.Add(new ChargeItem { Name = "CSDR Charges (on FV)", Percent = "0.01%", Amount = csdr.ToString("N2") });
            list.Add(new ChargeItem { Name = "CSDR VAT", Percent = "18%", Amount = csdrVat.ToString("N2") });
            list.Add(new ChargeItem { Name = "CMSA (on Consideration)", Percent = "0.01%", Amount = cmsa.ToString("N2") });
            return (consideration, brokerage, vatBrokerage, dse, dseVat, csdr, csdrVat, cmsa, totalCharges, list);
        }

        private void CalculateBrokerage()
        {
            if (!_isInitialized || MarketBox == null || MarketCategoryText == null) return;
            var selectedMarketItemContent = (MarketBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (string.IsNullOrEmpty(selectedMarketItemContent)) return;
            string selectedMarket = selectedMarketItemContent;
            double vatRate = 0.18;

            if (selectedMarket == "Bond - Secondary Market")
            {
                if (FaceValueBox == null || BondPriceBox == null || !double.TryParse(FaceValueBox.Text, out double fv) || fv <= 0 || !double.TryParse(BondPriceBox.Text, out double bondPrice) || bondPrice <= 0)
                {
                    if (ChargesGrid != null) ChargesGrid.ItemsSource = null; _currentChargesToReport.Clear();
                    if (BuyTotalText != null) BuyTotalText.Text = "Enter valid Face Value and Yield, then Calculate Price";
                    if (SellTotalText != null) SellTotalText.Text = "";
                    if (RemainingCashText != null) RemainingCashText.Text = "";
                    if (MarketCategoryText != null) MarketCategoryText.Text = "Bond Secondary Market"; return;
                }
                var result = CalculateSecondaryBondCharges(fv, bondPrice, vatRate);
                var finalChargesList = result.chargesList;
                finalChargesList.Add(new ChargeItem { Name = "TOTAL CHARGES", Percent = "-", Amount = result.totalCharges.ToString("N2") });
                bool isBuy = BondBuyRadio?.IsChecked == true;
                double finalSettlement = isBuy ? result.consideration + result.totalCharges : result.consideration - result.totalCharges;
                if (isBuy) { if (BuyTotalText != null) BuyTotalText.Text = $"BUY → Final Settlement: {finalSettlement:N2}"; if (SellTotalText != null) SellTotalText.Text = $"Consideration: {result.consideration:N2}"; }
                else { if (BuyTotalText != null) BuyTotalText.Text = $"SELL → Final Settlement: {finalSettlement:N2}"; if (SellTotalText != null) SellTotalText.Text = $"Consideration: {result.consideration:N2}"; }
                if (RemainingCashText != null && _currentBond != null)
                {
                    double couponAnnual = fv * _currentBond.couponRate / 100.0;
                    double couponHalf = couponAnnual / 2.0;
                    RemainingCashText.Text = $"Coupon to be received: {couponAnnual:N2} TZS p.a. ({couponHalf:N2} per half-year) | Coupon Rate {_currentBond.couponRate}% on FV {fv:N2}";
                }
                else if (RemainingCashText != null) RemainingCashText.Text = "";
                if (MarketCategoryText != null) MarketCategoryText.Text = "Bond - Secondary Market";
                _currentChargesToReport = new List<ChargeItem>(finalChargesList);
                if (ChargesGrid != null) ChargesGrid.ItemsSource = finalChargesList;
                return;
            }

            string effectiveMarket = selectedMarket == "Equity" ? "Equity" : "Bond";
            double finalTurnover = 0, finalPrice = 0, finalShares = 0, finalTotalRegulatoryAmount = 0;
            List<ChargeItem> finalChargesList2 = new List<ChargeItem>();

            if (_isCalculatingSharesMode)
            {
                if (TotalAmountBox == null || !double.TryParse(TotalAmountBox.Text, out double clientTotalBudget) || clientTotalBudget <= 0)
                {
                    if (ChargesGrid != null) ChargesGrid.ItemsSource = null; _currentChargesToReport.Clear();
                    if (BuyTotalText != null) BuyTotalText.Text = "BUY → Client Budget: -";
                    if (SellTotalText != null) SellTotalText.Text = "Shares to be obtained: -";
                    if (RemainingCashText != null) RemainingCashText.Text = "Change remaining: -";
                    if (MarketCategoryText != null) MarketCategoryText.Text = "Invalid Total Amount Input"; return;
                }
                if (PriceBox == null || !double.TryParse(PriceBox.Text, out finalPrice) || finalPrice <= 0)
                {
                    if (ChargesGrid != null) ChargesGrid.ItemsSource = null; _currentChargesToReport.Clear();
                    if (BuyTotalText != null) BuyTotalText.Text = $"BUY → Client Budget: {clientTotalBudget:N2}";
                    if (SellTotalText != null) SellTotalText.Text = "Shares to be obtained: -";
                    if (MarketCategoryText != null) MarketCategoryText.Text = "Enter a valid Price per share."; return;
                }
                double lowShares = 0, highShares = clientTotalBudget / finalPrice; finalShares = 0; double finalTotalConsideration = 0;
                for (int i = 0; i < 100; i++)
                {
                    double testShares = Math.Floor((lowShares + highShares) / 2);
                    if (testShares <= 0) { highShares = 0; break; }
                    double testTurnover = testShares * finalPrice;
                    var (b, vB, ds, dsV, c, f, cs, csV, _, _, _) = CalculateChargesFromTurnover(testTurnover, effectiveMarket, vatRate);
                    double totalCharges = b + vB + ds + dsV + c + f + cs + csV;
                    double totalCost = testTurnover + totalCharges;
                    if (totalCost <= clientTotalBudget) { finalShares = testShares; finalTotalConsideration = totalCost; lowShares = testShares + 1; } else { highShares = testShares - 1; }
                    if (lowShares > highShares) break;
                }
                if (finalShares <= 0) { if (ChargesGrid != null) ChargesGrid.ItemsSource = null; _currentChargesToReport.Clear(); if (BuyTotalText != null) BuyTotalText.Text = $"BUY → Client Budget: {clientTotalBudget:N2}"; if (SellTotalText != null) SellTotalText.Text = "Shares to be obtained: -"; if (MarketCategoryText != null) MarketCategoryText.Text = "Price too high"; return; }
                finalTurnover = finalShares * finalPrice;
                var (brokerage, vatOnBrokerage, dse, dseVat, cmsa, fidelity, csdr, csdrVat, calculatedChargesList_final, totalBasePercentage, totalBaseAmount) = CalculateChargesFromTurnover(finalTurnover, effectiveMarket, vatRate);
                finalTotalRegulatoryAmount = brokerage + vatOnBrokerage + dse + dseVat + cmsa + fidelity + csdr + csdrVat;
                finalChargesList2 = calculatedChargesList_final;
                finalChargesList2.Insert(0, new ChargeItem { Name = "Turnover", Percent = "-", Amount = finalTurnover.ToString("N2") });
                finalChargesList2.Insert(1, new ChargeItem { Name = "Price per Share", Percent = "-", Amount = finalPrice.ToString("N2") });
                finalChargesList2.Insert(2, new ChargeItem { Name = "Shares Purchased", Percent = "-", Amount = finalShares.ToString("N0") });
                if (finalTurnover > 0) finalChargesList2.Add(new ChargeItem { Name = "TOTAL", Percent = $"{totalBasePercentage * 100:N2}%", Amount = totalBaseAmount.ToString("N2") });
                double remainingCash = clientTotalBudget - finalTotalConsideration;
                if (BuyTotalText != null) BuyTotalText.Text = $"BUY → Client Budget: {clientTotalBudget:N2}";
                if (SellTotalText != null) SellTotalText.Text = $"Shares to be obtained: {finalShares:N0}";
                if (RemainingCashText != null) RemainingCashText.Text = $"Change remaining: {remainingCash:N2}";
                MarketCategoryText.Text = (effectiveMarket == "Equity" && finalTurnover > 10_000_000.0) ? "Equity (Tiered Brokerage)" : effectiveMarket;
            }
            else
            {
                if (PriceBox == null || SharesBox == null || !double.TryParse(PriceBox.Text, out finalPrice) || !double.TryParse(SharesBox.Text, out finalShares))
                {
                    if (ChargesGrid != null) ChargesGrid.ItemsSource = null; _currentChargesToReport.Clear();
                    if (BuyTotalText != null) BuyTotalText.Text = "BUY → Client Pays: -";
                    if (SellTotalText != null) SellTotalText.Text = "SELL → Client Receives: -";
                    if (RemainingCashText != null) RemainingCashText.Text = "";
                    if (MarketCategoryText != null) MarketCategoryText.Text = "Invalid Input"; return;
                }
                finalTurnover = finalPrice * finalShares;
                var (brokerage, vatOnBrokerage, dse, dseVat, cmsa, fidelity, csdr, csdrVat, calculatedChargesList, totalBasePercentage, totalBaseAmount) = CalculateChargesFromTurnover(finalTurnover, effectiveMarket, vatRate);
                finalTotalRegulatoryAmount = brokerage + vatOnBrokerage + dse + dseVat + cmsa + fidelity + csdr + csdrVat;
                finalChargesList2 = calculatedChargesList;
                finalChargesList2.Insert(0, new ChargeItem { Name = "Turnover", Percent = "-", Amount = finalTurnover.ToString("N2") });
                if (finalTurnover > 0) finalChargesList2.Add(new ChargeItem { Name = "TOTAL", Percent = $"{totalBasePercentage * 100:N2}%", Amount = totalBaseAmount.ToString("N2") });
                double buyTotal = finalTurnover + finalTotalRegulatoryAmount;
                double sellTotal = finalTurnover - finalTotalRegulatoryAmount;
                if (BuyTotalText != null) BuyTotalText.Text = $"BUY → Client Pays: {buyTotal:N2}";
                if (SellTotalText != null) SellTotalText.Text = $"SELL → Client Receives: {sellTotal:N2}";
                if (MarketCategoryText != null) MarketCategoryText.Text = (effectiveMarket == "Equity" && finalTurnover > 10_000_000.0) ? "Equity (Tiered Brokerage)" : effectiveMarket;
            }
            finalChargesList2.RemoveAll(item => item.Name == "TOTAL (TRA)");
            finalChargesList2.Add(new ChargeItem { Name = "TOTAL (TRA)", Percent = "-", Amount = finalTotalRegulatoryAmount.ToString("N2") });
            _currentChargesToReport = new List<ChargeItem>(finalChargesList2);
            if (ChargesGrid != null) ChargesGrid.ItemsSource = finalChargesList2;
        }

        private void btnPrintReport_Click(object sender, RoutedEventArgs e)
        {
            if (_currentChargesToReport == null || !_currentChargesToReport.Any()) { MessageBox.Show("No data available to print. Please perform a calculation first.", "No Data", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            StringBuilder reportBuilder = new StringBuilder();
            reportBuilder.AppendLine("BROKERAGE CALCULATION REPORT");
            reportBuilder.AppendLine("=========================================");
            reportBuilder.AppendLine($"Date Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            reportBuilder.AppendLine($"Calculation Mode: {(_isSecondaryBond ? "Bond Secondary Market" : _isCalculatingSharesMode ? "Calculate Shares (from Total Amount)" : "Calculate Charges (Price x Shares)")}");
            reportBuilder.AppendLine($"Market: {(MarketBox.SelectedItem as ComboBoxItem)?.Content?.ToString()}");
            if (_isSecondaryBond && BondNumberBox != null && !string.IsNullOrEmpty(BondNumberBox.Text))
            {
                reportBuilder.AppendLine($"Bond Number: {BondNumberBox.Text}");
                reportBuilder.AppendLine($"Coupon: {CouponRateText?.Text} Maturity: {MaturityDateText?.Text} Yield: {BondYieldBox?.Text}%");
                reportBuilder.AppendLine($"Clean: {_lastCleanPrice:F4} Accrued: {_lastAccruedInterest:F7} Dirty: {_lastDirtyPrice:F5} Cost: {_lastCost:N2}");
            }
            reportBuilder.AppendLine("=========================================");
            reportBuilder.AppendLine();
            reportBuilder.AppendLine(string.Format("{0,-30} | {1,-15} | {2,15}", "Charge", "%", "Amount (TZS)"));
            reportBuilder.AppendLine(new string('-', 65));
            foreach (var item in _currentChargesToReport) reportBuilder.AppendLine(string.Format("{0,-30} | {1,-15} | {2,15}", item.Name, item.Percent, double.TryParse(item.Amount, out double v) ? v.ToString("N2") : item.Amount));
            reportBuilder.AppendLine(new string('-', 65));
            reportBuilder.AppendLine();
            if (BuyTotalText != null) reportBuilder.AppendLine(BuyTotalText.Text);
            if (SellTotalText != null) reportBuilder.AppendLine(SellTotalText.Text);
            if (RemainingCashText != null && !string.IsNullOrEmpty(RemainingCashText.Text)) reportBuilder.AppendLine(RemainingCashText.Text);
            reportBuilder.AppendLine();
            reportBuilder.AppendLine("=========================================");
            reportBuilder.AppendLine("End of Report");
            try
            {
                string filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), $"BrokerageReport_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                File.WriteAllText(filePath, reportBuilder.ToString());
                MessageBox.Show($"Report saved to:\n{filePath}", "Report Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex) { MessageBox.Show($"Error saving report: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void CopyBuy_Click(object sender, RoutedEventArgs e)
        {
            if (BuyTotalText == null || string.IsNullOrEmpty(BuyTotalText.Text)) return;
            Clipboard.SetText(ExtractFigure(BuyTotalText.Text));
            ShowToast($"Copied: {ExtractFigure(BuyTotalText.Text)}");
            if (BtnCopyBuy != null) { BtnCopyBuy.Content = "✓ Copied!"; var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) }; t.Tick += (s, a) => { if (BtnCopyBuy != null) BtnCopyBuy.Content = "Copy"; t.Stop(); }; t.Start(); }
        }
        private void CopySell_Click(object sender, RoutedEventArgs e)
        {
            if (SellTotalText == null || string.IsNullOrEmpty(SellTotalText.Text)) return;
            Clipboard.SetText(ExtractFigure(SellTotalText.Text));
            ShowToast($"Copied: {ExtractFigure(SellTotalText.Text)}");
            if (BtnCopySell != null) { BtnCopySell.Content = "✓ Copied!"; var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) }; t.Tick += (s, a) => { if (BtnCopySell != null) BtnCopySell.Content = "Copy"; t.Stop(); }; t.Start(); }
        }
        private string ExtractFigure(string fullText) => fullText.Contains(":") ? fullText.Split(':').Last().Trim() : fullText;
        private void ShowToast(string message) { if (ToastText != null) ToastText.Text = message; if (ToastBorder != null) ToastBorder.Visibility = Visibility.Visible; _toastTimer?.Stop(); _toastTimer?.Start(); }
    }
}