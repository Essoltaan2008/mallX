import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import '../../core/theme/app_theme.dart';
import '../../data/services/api_service.dart';
import '../auth/login_screen.dart';
import '../home/main_nav_screen.dart';

// ══════════════════════════════════════════════════════════════════════════
//  ONBOARDING SCREEN — shown on first launch
// ══════════════════════════════════════════════════════════════════════════
class OnboardingScreen extends StatefulWidget {
  const OnboardingScreen({super.key});
  @override State<OnboardingScreen> createState() => _OnboardingScreenState();
}

class _OnboardingScreenState extends State<OnboardingScreen> {
  final _ctrl  = PageController();
  int   _page  = 0;

  static const _pages = [
    _OnboardPage(
      emoji:    '🏬',
      title:    'مرحباً في MallX',
      subtitle: 'تطبيق المول الذكي — تسوق، اطلب، احجز في مكان واحد',
      color:    Color(0xFF3B82F6),
    ),
    _OnboardPage(
      emoji:    '🛒',
      title:    'تسوق بسهولة',
      subtitle: 'أضف من محلات مختلفة في سلة واحدة ووصّلها لبيتك',
      color:    Color(0xFF10B981),
    ),
    _OnboardPage(
      emoji:    '⭐',
      title:    'اجمع نقاط الولاء',
      subtitle: 'كل طلب يكسبك نقاطاً — ارتقِ من Bronze لـ Gold واحصل على فوائد أكثر',
      color:    Color(0xFFF59E0B),
    ),
    _OnboardPage(
      emoji:    '🎁',
      title:    'عروض حصرية',
      subtitle: 'كوبونات خصم + فلاش سيل + إشعارات فور دخولك المول',
      color:    Color(0xFF8B5CF6),
    ),
  ];

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: SafeArea(
        child: Column(children: [
          // Skip
          Align(
            alignment: Alignment.centerLeft,
            child: TextButton(
              onPressed: _goToLogin,
              child: const Text('تخطي', style: TextStyle(color: AppTheme.textSec)),
            ),
          ),

          // Pages
          Expanded(
            child: PageView.builder(
              controller: _ctrl,
              itemCount: _pages.length,
              onPageChanged: (i) => setState(() => _page = i),
              itemBuilder: (_, i) => _pages[i],
            ),
          ),

          // Dots
          Row(mainAxisAlignment: MainAxisAlignment.center, children:
            List.generate(_pages.length, (i) => AnimatedContainer(
              duration: const Duration(milliseconds: 300),
              width: _page == i ? 24 : 8, height: 8,
              margin: const EdgeInsets.symmetric(horizontal: 3),
              decoration: BoxDecoration(
                color: _page == i
                    ? _pages[_page].color
                    : AppTheme.border,
                borderRadius: BorderRadius.circular(4)),
            ))),

          const SizedBox(height: 32),

          // Button
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 24),
            child: ElevatedButton(
              onPressed: () {
                if (_page < _pages.length - 1) {
                  _ctrl.nextPage(
                    duration: const Duration(milliseconds: 300),
                    curve: Curves.easeInOut);
                } else {
                  _goToLogin();
                }
              },
              style: ElevatedButton.styleFrom(
                backgroundColor: _pages[_page].color,
                minimumSize: const Size(double.infinity, 52)),
              child: Text(
                _page < _pages.length - 1 ? 'التالي' : 'ابدأ الآن',
                style: const TextStyle(fontWeight: FontWeight.w800, fontSize: 16)),
            ),
          ),
          const SizedBox(height: 24),
        ]),
      ),
    );
  }

  void _goToLogin() {
    Navigator.pushReplacement(context,
      MaterialPageRoute(builder: (_) => const LoginScreen()));
  }
}

class _OnboardPage extends StatelessWidget {
  final String emoji, title, subtitle;
  final Color  color;
  const _OnboardPage({required this.emoji, required this.title,
    required this.subtitle, required this.color});

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.all(32),
    child: Column(mainAxisAlignment: MainAxisAlignment.center, children: [
      // Emoji in gradient circle
      Container(
        width: 140, height: 140,
        decoration: BoxDecoration(
          shape: BoxShape.circle,
          gradient: RadialGradient(colors: [
            color.withOpacity(0.2),
            color.withOpacity(0.05),
          ])),
        child: Center(child: Text(emoji,
          style: const TextStyle(fontSize: 64)))),
      const SizedBox(height: 40),
      Text(title, style: const TextStyle(
        color: AppTheme.textPri, fontSize: 26, fontWeight: FontWeight.w900),
        textAlign: TextAlign.center),
      const SizedBox(height: 16),
      Text(subtitle, style: const TextStyle(
        color: AppTheme.textSec, fontSize: 16, height: 1.6),
        textAlign: TextAlign.center),
    ]),
  );
}

// ══════════════════════════════════════════════════════════════════════════
//  WALLET SCREEN
// ══════════════════════════════════════════════════════════════════════════
class WalletScreen extends StatefulWidget {
  final String mallId;
  const WalletScreen({super.key, required this.mallId});
  @override State<WalletScreen> createState() => _WalletScreenState();
}

class _WalletScreenState extends State<WalletScreen> {
  final _api = ApiService();
  Map? _wallet;
  bool _loading = true;
  final _amountCtrl = TextEditingController();

  @override
  void initState() { super.initState(); _load(); }

  @override void dispose() { _amountCtrl.dispose(); super.dispose(); }

  Future<void> _load() async {
    try {
      final res = await _api.get('/mall/wallet?mallId=${widget.mallId}');
      setState(() { _wallet = res.data['data']; _loading = false; });
    } catch (_) { setState(() => _loading = false); }
  }

  Future<void> _topUp(double amount) async {
    try {
      await _api.post('/mall/wallet/topup?mallId=${widget.mallId}',
        data: {'amount': amount, 'gateway': 'Cash', 'gatewayRef': null});
      _load();
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('✅ تم شحن ${amount.toStringAsFixed(0)} ج.م بنجاح!'),
          backgroundColor: AppTheme.secondary));
    } catch (_) {}
  }

  @override
  Widget build(BuildContext context) {
    final balance = (_wallet?['balance'] as num?)?.toDouble() ?? 0;
    final txns    = List<Map>.from(_wallet?['recentTransactions'] ?? []);

    return Scaffold(
      appBar: AppBar(title: const Text('محفظتي')),
      body: _loading
        ? const Center(child: CircularProgressIndicator())
        : SingleChildScrollView(
          padding: const EdgeInsets.all(20),
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [

            // Balance card
            Container(
              width: double.infinity, padding: const EdgeInsets.all(28),
              decoration: BoxDecoration(
                gradient: const LinearGradient(
                  colors: [Color(0xFF1E3A5F), Color(0xFF0A0F1A)],
                  begin: Alignment.topLeft, end: Alignment.bottomRight),
                borderRadius: BorderRadius.circular(24),
                border: Border.all(color: AppTheme.primary.withOpacity(0.3))),
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                const Text('الرصيد المتاح', style: TextStyle(color: AppTheme.textSec, fontSize: 13)),
                const SizedBox(height: 10),
                Row(crossAxisAlignment: CrossAxisAlignment.end, children: [
                  Text(balance.toStringAsFixed(2),
                    style: const TextStyle(color: Colors.white, fontSize: 44, fontWeight: FontWeight.w900)),
                  const SizedBox(width: 8),
                  const Padding(padding: EdgeInsets.only(bottom: 8),
                    child: Text('ج.م', style: TextStyle(color: AppTheme.textSec, fontSize: 16))),
                ]),
                const SizedBox(height: 16),
                Row(children: [
                  _walletStat('إجمالي الشحن', '${_wallet?['totalToppedUp'] ?? 0} ج.م'),
                  const SizedBox(width: 24),
                  _walletStat('إجمالي الإنفاق', '${_wallet?['totalSpent'] ?? 0} ج.م'),
                ]),
              ])),

            const SizedBox(height: 24),

            // Quick top-up
            const Text('شحن سريع', style: TextStyle(
              color: AppTheme.textPri, fontWeight: FontWeight.w700, fontSize: 15)),
            const SizedBox(height: 12),
            Wrap(spacing: 10, children: [50.0, 100.0, 200.0, 500.0].map((amt) =>
              GestureDetector(
                onTap: () => _topUp(amt),
                child: Container(
                  padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 12),
                  decoration: BoxDecoration(
                    color: AppTheme.card, borderRadius: BorderRadius.circular(12),
                    border: Border.all(color: AppTheme.border)),
                  child: Text('${amt.toStringAsFixed(0)} ج.م',
                    style: const TextStyle(color: AppTheme.primary, fontWeight: FontWeight.w700))))).toList()),

            const SizedBox(height: 16),

            // Custom amount
            Row(children: [
              Expanded(child: TextField(
                controller: _amountCtrl,
                keyboardType: TextInputType.number,
                inputFormatters: [FilteringTextInputFormatter.digitsOnly],
                decoration: const InputDecoration(
                  hintText: 'مبلغ آخر...',
                  prefixText: 'ج.م  ',
                  prefixStyle: TextStyle(color: AppTheme.textSec)),
              )),
              const SizedBox(width: 12),
              ElevatedButton(
                onPressed: () {
                  final amt = double.tryParse(_amountCtrl.text);
                  if (amt != null && amt >= 10) _topUp(amt);
                },
                style: ElevatedButton.styleFrom(
                  padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 14)),
                child: const Text('شحن')),
            ]),

            const SizedBox(height: 28),

            // Transactions
            const Text('المعاملات الأخيرة', style: TextStyle(
              color: AppTheme.textPri, fontWeight: FontWeight.w700, fontSize: 15)),
            const SizedBox(height: 12),

            if (txns.isEmpty)
              const Center(child: Text('لا توجد معاملات بعد',
                style: TextStyle(color: AppTheme.textSec)))
            else
              ...txns.map((t) {
                final amount   = (t['amount'] as num?)?.toDouble() ?? 0;
                final isCredit = amount > 0;
                return Container(
                  margin: const EdgeInsets.only(bottom: 8),
                  padding: const EdgeInsets.all(14),
                  decoration: BoxDecoration(color: AppTheme.card,
                    borderRadius: BorderRadius.circular(12),
                    border: Border.all(color: AppTheme.border)),
                  child: Row(children: [
                    Container(width: 40, height: 40,
                      decoration: BoxDecoration(
                        color: (isCredit ? AppTheme.secondary : AppTheme.error).withOpacity(0.1),
                        borderRadius: BorderRadius.circular(12)),
                      child: Icon(isCredit ? Icons.add : Icons.remove,
                        color: isCredit ? AppTheme.secondary : AppTheme.error)),
                    const SizedBox(width: 12),
                    Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                      Text(t['typeAr'] ?? '', style: const TextStyle(
                        color: AppTheme.textPri, fontWeight: FontWeight.w600, fontSize: 13)),
                      if (t['description'] != null)
                        Text(t['description'], style: const TextStyle(
                          color: AppTheme.textSec, fontSize: 11), maxLines: 1, overflow: TextOverflow.ellipsis),
                    ])),
                    Text('${isCredit ? "+" : ""}${amount.toStringAsFixed(2)} ج.م',
                      style: TextStyle(
                        color: isCredit ? AppTheme.secondary : AppTheme.error,
                        fontWeight: FontWeight.w800, fontSize: 14)),
                  ]));
              }),
          ]));
  }

  Widget _walletStat(String label, String val) => Column(
    crossAxisAlignment: CrossAxisAlignment.start, children: [
    Text(label, style: const TextStyle(color: AppTheme.textSec, fontSize: 11)),
    Text(val, style: const TextStyle(color: Colors.white, fontWeight: FontWeight.w700, fontSize: 13)),
  ]);
}

// ══════════════════════════════════════════════════════════════════════════
//  REFERRAL SCREEN
// ══════════════════════════════════════════════════════════════════════════
class ReferralScreen extends StatefulWidget {
  final String mallId;
  const ReferralScreen({super.key, required this.mallId});
  @override State<ReferralScreen> createState() => _ReferralScreenState();
}

class _ReferralScreenState extends State<ReferralScreen> {
  final _api   = ApiService();
  Map?  _info;
  bool  _loading = true;
  bool  _copied  = false;

  @override
  void initState() { super.initState(); _load(); }

  Future<void> _load() async {
    try {
      final res = await _api.get('/mall/referral/code?mallId=${widget.mallId}');
      setState(() { _info = res.data['data']; _loading = false; });
    } catch (_) { setState(() => _loading = false); }
  }

  Future<void> _copy(String text) async {
    await Clipboard.setData(ClipboardData(text: text));
    setState(() => _copied = true);
    Future.delayed(const Duration(seconds: 2), () {
      if (mounted) setState(() => _copied = false);
    });
  }

  @override
  Widget build(BuildContext context) {
    final code    = _info?['code'] as String? ?? '---';
    final uses    = _info?['usesCount'] as int? ?? 0;
    final refPts  = _info?['referrerPts'] as int? ?? 200;
    final reePts  = _info?['refereePts'] as int? ?? 100;
    final disc    = _info?['refereeDiscount'] as num? ?? 10;
    final shareMsg= _info?['shareMessage'] as String? ?? '';

    return Scaffold(
      appBar: AppBar(title: const Text('أحِل صديقاً')),
      body: _loading
        ? const Center(child: CircularProgressIndicator())
        : SingleChildScrollView(
          padding: const EdgeInsets.all(20),
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [

            // Hero
            Container(
              width: double.infinity, padding: const EdgeInsets.all(28),
              decoration: BoxDecoration(
                gradient: LinearGradient(colors: [
                  AppTheme.purple.withOpacity(0.3),
                  AppTheme.primary.withOpacity(0.2),
                ], begin: Alignment.topRight, end: Alignment.bottomLeft),
                borderRadius: BorderRadius.circular(24),
                border: Border.all(color: AppTheme.purple.withOpacity(0.3))),
              child: Column(children: [
                const Text('🎁', style: TextStyle(fontSize: 56)),
                const SizedBox(height: 16),
                const Text('أحِل أصدقاءك واكسب مكافآت!',
                  style: TextStyle(color: Colors.white, fontSize: 20, fontWeight: FontWeight.w800),
                  textAlign: TextAlign.center),
                const SizedBox(height: 8),
                Text('كل إحالة ناجحة تكسبك $refPts نقطة، وصديقك يحصل على $reePts نقطة + خصم $disc%',
                  style: const TextStyle(color: AppTheme.textSec, fontSize: 13, height: 1.5),
                  textAlign: TextAlign.center),
              ])),

            const SizedBox(height: 24),

            // Code
            const Text('كود الإحالة الخاص بك', style: TextStyle(
              color: AppTheme.textPri, fontWeight: FontWeight.w700, fontSize: 15)),
            const SizedBox(height: 10),
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 16),
              decoration: BoxDecoration(
                color: AppTheme.card, borderRadius: BorderRadius.circular(14),
                border: Border.all(color: AppTheme.primary.withOpacity(0.4))),
              child: Row(children: [
                Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  Text(code, style: const TextStyle(
                    color: AppTheme.primary, fontSize: 28, fontWeight: FontWeight.w900,
                    letterSpacing: 4)),
                  Text('استُخدم $uses مرة', style: const TextStyle(
                    color: AppTheme.textSec, fontSize: 12)),
                ])),
                GestureDetector(
                  onTap: () => _copy(code),
                  child: Container(
                    padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
                    decoration: BoxDecoration(
                      color: _copied ? AppTheme.secondary : AppTheme.primary,
                      borderRadius: BorderRadius.circular(10)),
                    child: Text(_copied ? '✓ تم' : 'نسخ',
                      style: const TextStyle(color: Colors.white, fontWeight: FontWeight.w700)))),
              ])),

            const SizedBox(height: 16),

            // Share button
            ElevatedButton.icon(
              onPressed: () => _copy(shareMsg),
              icon: const Icon(Icons.share),
              label: const Text('شارك الكود مع أصدقائك'),
              style: ElevatedButton.styleFrom(
                backgroundColor: const Color(0xFF8B5CF6),
                minimumSize: const Size(double.infinity, 52))),

            const SizedBox(height: 28),

            // How it works
            const Text('كيف يعمل؟', style: TextStyle(
              color: AppTheme.textPri, fontWeight: FontWeight.w700, fontSize: 15)),
            const SizedBox(height: 12),
            ...[
              ('1️⃣', 'شارك كودك', 'أرسل الكود لأصدقائك عبر واتساب أو أي وسيلة'),
              ('2️⃣', 'صديقك يسجل', 'يُدخل الكود عند التسجيل ويحصل على مكافأته فوراً'),
              ('3️⃣', 'كلاكما يكسب', 'بعد أول طلب لصديقك أنت تحصل على $refPts نقطة!'),
            ].map((step) => Container(
              margin: const EdgeInsets.only(bottom: 10),
              padding: const EdgeInsets.all(14),
              decoration: BoxDecoration(color: AppTheme.card, borderRadius: BorderRadius.circular(12),
                border: Border.all(color: AppTheme.border)),
              child: Row(children: [
                Text(step.$1, style: const TextStyle(fontSize: 24)),
                const SizedBox(width: 14),
                Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  Text(step.$2, style: const TextStyle(
                    color: AppTheme.textPri, fontWeight: FontWeight.w700)),
                  Text(step.$3, style: const TextStyle(
                    color: AppTheme.textSec, fontSize: 12)),
                ])),
              ]))),
          ]));
  }
}
