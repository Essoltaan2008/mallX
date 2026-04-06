import 'dart:async';
import 'package:flutter/material.dart';
import '../../core/theme/app_theme.dart';
import '../../data/services/api_service.dart';

// ──────────────────────────────────────────────────────────────────────────
//  MODELS
// ──────────────────────────────────────────────────────────────────────────
class LoyaltyWallet {
  final int     availablePoints, lifetimePoints, pointsToNext;
  final String  tier, tierAr;
  final double  egpValue;
  final String? nextTier;
  final List<PointsTxn> recent;
  final Map<String, dynamic> benefits;

  LoyaltyWallet({required this.availablePoints, required this.lifetimePoints,
    required this.pointsToNext, required this.tier, required this.tierAr,
    required this.egpValue, this.nextTier, required this.recent,
    required this.benefits});

  factory LoyaltyWallet.fromJson(Map j) => LoyaltyWallet(
    availablePoints: j['availablePoints'] ?? 0,
    lifetimePoints:  j['lifetimePoints']  ?? 0,
    pointsToNext:    j['pointsToNext']    ?? 0,
    tier:    j['tier']   ?? 'Bronze',
    tierAr:  j['tierAr'] ?? 'برونزي',
    egpValue: (j['egpValue'] as num?)?.toDouble() ?? 0,
    nextTier: j['nextTier'],
    recent:   (j['recentTransactions'] as List? ?? []).map((t) => PointsTxn.fromJson(t)).toList(),
    benefits: j['benefits'] ?? {},
  );
}

class PointsTxn {
  final String id, source, sourceAr;
  final int points, balanceAfter;
  final String? description;
  final DateTime createdAt;

  PointsTxn({required this.id, required this.source, required this.sourceAr,
    required this.points, required this.balanceAfter,
    this.description, required this.createdAt});

  bool get isEarning => points > 0;

  factory PointsTxn.fromJson(Map j) => PointsTxn(
    id: j['id'], source: j['source'], sourceAr: j['sourceAr'],
    points: j['points'], balanceAfter: j['balanceAfter'],
    description: j['description'],
    createdAt: DateTime.parse(j['createdAt']));
}

class FlashSaleModel {
  final String id, title;
  final String? titleAr, bannerUrl;
  final double originalPrice, flashPrice, discountPct;
  final int remaining, secondsLeft;
  final bool isLive;

  FlashSaleModel({required this.id, required this.title, this.titleAr,
    this.bannerUrl, required this.originalPrice, required this.flashPrice,
    required this.discountPct, required this.remaining,
    required this.secondsLeft, required this.isLive});

  factory FlashSaleModel.fromJson(Map j) => FlashSaleModel(
    id: j['id'], title: j['title'], titleAr: j['titleAr'],
    bannerUrl: j['bannerUrl'],
    originalPrice: (j['originalPrice'] as num?)?.toDouble() ?? 0,
    flashPrice: (j['flashPrice'] as num).toDouble(),
    discountPct: (j['discountPct'] as num?)?.toDouble() ?? 0,
    remaining: j['remaining'] ?? 0, secondsLeft: j['secondsLeft'] ?? 0,
    isLive: j['isLive'] ?? false);
}

class CouponModel {
  final String id, code, name, discountType, validTo;
  final double discountValue, minOrderValue;
  final String? description, minTier;
  final bool isExpiringSoon;

  CouponModel({required this.id, required this.code, required this.name,
    required this.discountType, required this.validTo, required this.discountValue,
    required this.minOrderValue, this.description, this.minTier,
    required this.isExpiringSoon});

  factory CouponModel.fromJson(Map j) => CouponModel(
    id: j['id'], code: j['code'], name: j['name'],
    discountType: j['discountType'], validTo: j['validTo'],
    discountValue: (j['discountValue'] as num).toDouble(),
    minOrderValue: (j['minOrderValue'] as num?)?.toDouble() ?? 0,
    description: j['description'], minTier: j['minTier'],
    isExpiringSoon: j['isExpiringSoon'] ?? false);

  String get discountLabel => discountType == 'Percentage'
    ? '${discountValue.toStringAsFixed(0)}% خصم'
    : '${discountValue.toStringAsFixed(0)} ج.م خصم';
}

// ══════════════════════════════════════════════════════════════════════════
//  LOYALTY WALLET SCREEN
// ══════════════════════════════════════════════════════════════════════════
class LoyaltyWalletScreen extends StatefulWidget {
  const LoyaltyWalletScreen({super.key});
  @override State<LoyaltyWalletScreen> createState() => _LoyaltyWalletScreenState();
}

class _LoyaltyWalletScreenState extends State<LoyaltyWalletScreen>
    with SingleTickerProviderStateMixin {
  final _api = ApiService();
  LoyaltyWallet? _wallet;
  bool _loading = true;
  late AnimationController _animCtrl;
  late Animation<double> _progressAnim;

  @override
  void initState() {
    super.initState();
    _animCtrl = AnimationController(vsync: this, duration: const Duration(milliseconds: 1200));
    _progressAnim = Tween(begin: 0.0, end: 1.0).animate(
      CurvedAnimation(parent: _animCtrl, curve: Curves.easeOutCubic));
    _load();
  }

  @override
  void dispose() { _animCtrl.dispose(); super.dispose(); }

  Future<void> _load() async {
    try {
      final res = await _api.get('/mall/loyalty/wallet');
      setState(() {
        _wallet  = LoyaltyWallet.fromJson(res.data['data']);
        _loading = false;
      });
      _animCtrl.forward();
    } catch (_) { setState(() => _loading = false); }
  }

  Color _tierColor(String tier) => switch (tier) {
    'Gold'   => const Color(0xFFF59E0B),
    'Silver' => const Color(0xFF94A3B8),
    _        => const Color(0xFFCD7F32),
  };

  double _tierProgress() {
    if (_wallet == null) return 0;
    final pts = _wallet!.lifetimePoints;
    if (pts >= 5000) return 1.0;
    if (pts >= 1000) return (pts - 1000) / 4000;
    return pts / 1000;
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('محفظة النقاط')),
      body: _loading
        ? const Center(child: CircularProgressIndicator())
        : _wallet == null
          ? const Center(child: Text('تعذر تحميل البيانات'))
          : RefreshIndicator(
              onRefresh: _load,
              child: SingleChildScrollView(
                physics: const AlwaysScrollableScrollPhysics(),
                child: Column(children: [

                  // ── Hero Card ───────────────────────────────────────
                  Container(
                    margin: const EdgeInsets.all(16),
                    padding: const EdgeInsets.all(24),
                    decoration: BoxDecoration(
                      gradient: LinearGradient(
                        colors: [
                          _tierColor(_wallet!.tier).withOpacity(0.3),
                          AppTheme.surface,
                          AppTheme.bg,
                        ],
                        begin: Alignment.topLeft, end: Alignment.bottomRight,
                      ),
                      borderRadius: BorderRadius.circular(24),
                      border: Border.all(color: _tierColor(_wallet!.tier).withOpacity(0.4)),
                    ),
                    child: Column(children: [
                      Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
                        Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                          Text(_wallet!.tierAr, style: TextStyle(
                            color: _tierColor(_wallet!.tier),
                            fontWeight: FontWeight.w700, fontSize: 16)),
                          const SizedBox(height: 4),
                          const Text('نقاطي المتاحة', style: TextStyle(
                            color: AppTheme.textSec, fontSize: 13)),
                        ]),
                        // Tier badge
                        Container(
                          width: 56, height: 56,
                          decoration: BoxDecoration(
                            shape: BoxShape.circle,
                            color: _tierColor(_wallet!.tier).withOpacity(0.15),
                            border: Border.all(color: _tierColor(_wallet!.tier), width: 2)),
                          child: Center(child: Text(
                            _wallet!.tier == 'Gold' ? '🥇'
                              : _wallet!.tier == 'Silver' ? '🥈' : '🥉',
                            style: const TextStyle(fontSize: 24))),
                        ),
                      ]),
                      const SizedBox(height: 16),
                      AnimatedBuilder(
                        animation: _progressAnim,
                        builder: (_, __) => Text(
                          '${(_wallet!.availablePoints * _progressAnim.value).toInt()}',
                          style: TextStyle(
                            color: _tierColor(_wallet!.tier),
                            fontSize: 56, fontWeight: FontWeight.w900)),
                      ),
                      Text('نقطة = ${_wallet!.egpValue.toStringAsFixed(2)} ج.م',
                        style: const TextStyle(color: AppTheme.textSec, fontSize: 13)),

                      const SizedBox(height: 20),

                      // Progress to next tier
                      if (_wallet!.nextTier != null) ...[
                        Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
                          Text(_wallet!.tier, style: const TextStyle(
                            color: AppTheme.textSec, fontSize: 11)),
                          Text('${_wallet!.pointsToNext} نقطة للـ ${_wallet!.nextTier}',
                            style: const TextStyle(color: AppTheme.textSec, fontSize: 11)),
                          Text(_wallet!.nextTier!, style: const TextStyle(
                            color: AppTheme.textSec, fontSize: 11)),
                        ]),
                        const SizedBox(height: 6),
                        AnimatedBuilder(
                          animation: _progressAnim,
                          builder: (_, __) => ClipRRect(
                            borderRadius: BorderRadius.circular(4),
                            child: LinearProgressIndicator(
                              value: _tierProgress() * _progressAnim.value,
                              backgroundColor: AppTheme.border,
                              valueColor: AlwaysStoppedAnimation(_tierColor(_wallet!.tier)),
                              minHeight: 8)),
                        ),
                      ] else
                        Container(
                          padding: const EdgeInsets.all(10),
                          decoration: BoxDecoration(
                            color: _tierColor(_wallet!.tier).withOpacity(0.1),
                            borderRadius: BorderRadius.circular(10)),
                          child: Row(mainAxisAlignment: MainAxisAlignment.center, children: [
                            Icon(Icons.workspace_premium,
                              color: _tierColor(_wallet!.tier), size: 18),
                            const SizedBox(width: 8),
                            Text('أنت في أعلى مستوى! 🏆', style: TextStyle(
                              color: _tierColor(_wallet!.tier),
                              fontWeight: FontWeight.w700)),
                          ])),
                    ]),
                  ),

                  // ── Benefits ─────────────────────────────────────────
                  _section('🎁 مزايا مستواك'),
                  Container(
                    margin: const EdgeInsets.symmetric(horizontal: 16),
                    padding: const EdgeInsets.all(16),
                    decoration: BoxDecoration(color: AppTheme.card,
                      borderRadius: BorderRadius.circular(14),
                      border: Border.all(color: AppTheme.border)),
                    child: Column(children: [
                      _benefitRow(Icons.bolt, '${_wallet!.benefits["multiplier"]}x نقاط', 'على كل مشترياتك'),
                      if (_wallet!.benefits["freeDelivery"] == true)
                        _benefitRow(Icons.delivery_dining, 'توصيل مجاني', 'على جميع الطلبات'),
                      _benefitRow(Icons.swap_horiz, 'استبدال النقاط', 'حتى 20% من قيمة أي طلب'),
                    ]),
                  ),

                  const SizedBox(height: 20),

                  // ── Recent Transactions ──────────────────────────────
                  _section('📋 آخر المعاملات'),
                  ...(_wallet!.recent.map((t) => _TxnTile(txn: t))),
                  const SizedBox(height: 20),
                ]),
              ),
            ),
    );
  }

  Widget _section(String title) => Padding(
    padding: const EdgeInsets.fromLTRB(16, 0, 16, 10),
    child: Text(title, style: const TextStyle(
      color: AppTheme.textPri, fontWeight: FontWeight.w700, fontSize: 16)));

  Widget _benefitRow(IconData icon, String label, String sub) => Padding(
    padding: const EdgeInsets.only(bottom: 12),
    child: Row(children: [
      Container(width: 36, height: 36,
        decoration: BoxDecoration(color: AppTheme.primary.withOpacity(0.1),
          borderRadius: BorderRadius.circular(10)),
        child: Icon(icon, color: AppTheme.primary, size: 18)),
      const SizedBox(width: 12),
      Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Text(label, style: const TextStyle(color: AppTheme.textPri, fontWeight: FontWeight.w600)),
        Text(sub, style: const TextStyle(color: AppTheme.textSec, fontSize: 12)),
      ]),
    ]));
}

class _TxnTile extends StatelessWidget {
  final PointsTxn txn;
  const _TxnTile({required this.txn});

  @override
  Widget build(BuildContext context) => Container(
    margin: const EdgeInsets.symmetric(horizontal: 16, vertical: 4),
    padding: const EdgeInsets.all(14),
    decoration: BoxDecoration(color: AppTheme.card,
      borderRadius: BorderRadius.circular(12),
      border: Border.all(color: AppTheme.border)),
    child: Row(children: [
      Container(width: 36, height: 36,
        decoration: BoxDecoration(
          color: (txn.isEarning ? AppTheme.secondary : AppTheme.error).withOpacity(0.1),
          borderRadius: BorderRadius.circular(10)),
        child: Icon(txn.isEarning ? Icons.add : Icons.remove,
          color: txn.isEarning ? AppTheme.secondary : AppTheme.error, size: 18)),
      const SizedBox(width: 12),
      Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Text(txn.sourceAr, style: const TextStyle(
          color: AppTheme.textPri, fontWeight: FontWeight.w600, fontSize: 13)),
        if (txn.description != null)
          Text(txn.description!, style: const TextStyle(color: AppTheme.textSec, fontSize: 11),
            maxLines: 1, overflow: TextOverflow.ellipsis),
      ])),
      Column(crossAxisAlignment: CrossAxisAlignment.end, children: [
        Text('${txn.isEarning ? "+" : ""}${txn.points}',
          style: TextStyle(
            color: txn.isEarning ? AppTheme.secondary : AppTheme.error,
            fontWeight: FontWeight.w800, fontSize: 15)),
        Text('${txn.balanceAfter} رصيد',
          style: const TextStyle(color: AppTheme.textSec, fontSize: 10)),
      ]),
    ]),
  );
}

// ══════════════════════════════════════════════════════════════════════════
//  PROMOTIONS SCREEN
// ══════════════════════════════════════════════════════════════════════════
class PromotionsScreen extends StatefulWidget {
  const PromotionsScreen({super.key});
  @override State<PromotionsScreen> createState() => _PromotionsScreenState();
}

class _PromotionsScreenState extends State<PromotionsScreen> {
  final _api = ApiService();
  List<FlashSaleModel> _flash   = [];
  List<CouponModel>    _coupons = [];
  bool _loading = true;
  late Timer _timer;

  @override
  void initState() {
    super.initState();
    _load();
    _timer = Timer.periodic(const Duration(seconds: 1), (_) {
      if (mounted) setState(() {}); // rebuild for countdown
    });
  }

  @override
  void dispose() { _timer.cancel(); super.dispose(); }

  Future<void> _load() async {
    try {
      final res = await _api.get('/mall/promotions');
      final data = res.data['data'];
      setState(() {
        _flash   = (data['flashSales'] as List? ?? []).map((f) => FlashSaleModel.fromJson(f)).toList();
        _coupons = (data['coupons']    as List? ?? []).map((c) => CouponModel.fromJson(c)).toList();
        _loading = false;
      });
    } catch (_) { setState(() => _loading = false); }
  }

  String _formatCountdown(int seconds) {
    final h = seconds ~/ 3600;
    final m = (seconds % 3600) ~/ 60;
    final s = seconds % 60;
    if (h > 0) return '${h}س ${m}د ${s}ث';
    return '${m}د ${s}ث';
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('العروض والكوبونات')),
      body: _loading
        ? const Center(child: CircularProgressIndicator())
        : RefreshIndicator(
            onRefresh: _load,
            child: ListView(padding: const EdgeInsets.all(16), children: [

              // ── Flash Sales ────────────────────────────────────────
              if (_flash.isNotEmpty) ...[
                Row(children: [
                  const Icon(Icons.flash_on, color: Color(0xFFF59E0B), size: 20),
                  const SizedBox(width: 6),
                  const Text('عروض لفترة محدودة', style: TextStyle(
                    color: AppTheme.textPri, fontWeight: FontWeight.w800, fontSize: 16)),
                ]),
                const SizedBox(height: 12),
                ..._flash.map((f) => _FlashCard(
                  flash: f,
                  countdown: _formatCountdown(
                    f.secondsLeft > 0 ? f.secondsLeft - 1 : 0),
                )),
                const SizedBox(height: 20),
              ],

              // ── Coupons ───────────────────────────────────────────
              if (_coupons.isNotEmpty) ...[
                const Row(children: [
                  Icon(Icons.local_offer_outlined, color: AppTheme.primary, size: 20),
                  SizedBox(width: 6),
                  Text('كوبونات الخصم', style: TextStyle(
                    color: AppTheme.textPri, fontWeight: FontWeight.w800, fontSize: 16)),
                ]),
                const SizedBox(height: 12),
                ..._coupons.map((c) => _CouponCard(coupon: c)),
              ],

              if (_flash.isEmpty && _coupons.isEmpty)
                const Center(child: Padding(
                  padding: EdgeInsets.only(top: 48),
                  child: Text('لا توجد عروض متاحة حالياً',
                    style: TextStyle(color: AppTheme.textSec)))),
            ]),
          ),
    );
  }
}

class _FlashCard extends StatelessWidget {
  final FlashSaleModel flash;
  final String countdown;
  const _FlashCard({required this.flash, required this.countdown});

  @override
  Widget build(BuildContext context) => Container(
    margin: const EdgeInsets.only(bottom: 12),
    decoration: BoxDecoration(
      color: AppTheme.card,
      borderRadius: BorderRadius.circular(16),
      border: Border.all(color: const Color(0xFFF59E0B).withOpacity(0.4)),
    ),
    child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
      // Countdown banner
      Container(
        width: double.infinity,
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
        decoration: BoxDecoration(
          color: const Color(0xFFF59E0B).withOpacity(0.15),
          borderRadius: const BorderRadius.only(
            topRight: Radius.circular(16), topLeft: Radius.circular(16))),
        child: Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
          const Row(children: [
            Icon(Icons.flash_on, color: Color(0xFFF59E0B), size: 16),
            SizedBox(width: 4),
            Text('ينتهي خلال', style: TextStyle(color: Color(0xFFF59E0B), fontSize: 12)),
          ]),
          Text(countdown, style: const TextStyle(
            color: Color(0xFFF59E0B), fontWeight: FontWeight.w800, fontSize: 14)),
          Text('متبقي ${flash.remaining}', style: const TextStyle(
            color: Color(0xFFF59E0B), fontSize: 12)),
        ]),
      ),
      Padding(padding: const EdgeInsets.all(16), child: Row(children: [
        Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Text(flash.titleAr ?? flash.title, style: const TextStyle(
            color: AppTheme.textPri, fontWeight: FontWeight.w700, fontSize: 15)),
          const SizedBox(height: 8),
          Row(children: [
            Text('${flash.flashPrice.toStringAsFixed(0)} ج.م',
              style: const TextStyle(color: AppTheme.primary,
                fontWeight: FontWeight.w900, fontSize: 20)),
            const SizedBox(width: 10),
            Text('${flash.originalPrice.toStringAsFixed(0)} ج.م',
              style: const TextStyle(color: AppTheme.textSec,
                fontSize: 14, decoration: TextDecoration.lineThrough)),
          ]),
        ])),
        Container(
          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
          decoration: BoxDecoration(
            color: const Color(0xFFF59E0B),
            borderRadius: BorderRadius.circular(10)),
          child: Text('-${flash.discountPct.toStringAsFixed(0)}%',
            style: const TextStyle(color: Colors.black,
              fontWeight: FontWeight.w900, fontSize: 16))),
      ])),
    ]),
  );
}

class _CouponCard extends StatelessWidget {
  final CouponModel coupon;
  const _CouponCard({required this.coupon});

  @override
  Widget build(BuildContext context) => Container(
    margin: const EdgeInsets.only(bottom: 10),
    decoration: BoxDecoration(
      color: AppTheme.card, borderRadius: BorderRadius.circular(14),
      border: Border.all(color: coupon.isExpiringSoon
        ? AppTheme.error.withOpacity(0.4) : AppTheme.border)),
    child: Row(children: [
      // Discount badge
      Container(
        width: 80,
        padding: const EdgeInsets.symmetric(vertical: 20),
        decoration: BoxDecoration(
          color: AppTheme.primary.withOpacity(0.1),
          borderRadius: const BorderRadius.only(
            topRight: Radius.circular(14), bottomRight: Radius.circular(14))),
        child: Column(mainAxisAlignment: MainAxisAlignment.center, children: [
          Text(coupon.discountLabel.split(' ').first,
            style: const TextStyle(color: AppTheme.primary,
              fontWeight: FontWeight.w900, fontSize: 18)),
          Text(coupon.discountLabel.split(' ').skip(1).join(' '),
            style: const TextStyle(color: AppTheme.primary, fontSize: 10),
            textAlign: TextAlign.center),
        ])),
      const SizedBox(width: 12),
      Expanded(child: Padding(
        padding: const EdgeInsets.symmetric(vertical: 14),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Text(coupon.name, style: const TextStyle(
            color: AppTheme.textPri, fontWeight: FontWeight.w700)),
          if (coupon.description != null)
            Text(coupon.description!, style: const TextStyle(
              color: AppTheme.textSec, fontSize: 12)),
          const SizedBox(height: 6),
          Row(children: [
            Container(padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
              decoration: BoxDecoration(color: AppTheme.surface,
                borderRadius: BorderRadius.circular(6),
                border: Border.all(color: AppTheme.border)),
              child: Text(coupon.code, style: const TextStyle(
                color: AppTheme.textPri, fontWeight: FontWeight.w800,
                fontSize: 12, letterSpacing: 1))),
            const SizedBox(width: 8),
            if (coupon.isExpiringSoon)
              const Text('⚠️ ينتهي قريباً', style: TextStyle(
                color: AppTheme.error, fontSize: 10)),
          ]),
          const SizedBox(height: 4),
          Text('صالح حتى ${coupon.validTo}',
            style: const TextStyle(color: AppTheme.textSec, fontSize: 10)),
        ]),
      )),
      Padding(
        padding: const EdgeInsets.all(12),
        child: GestureDetector(
          onTap: () {
            // Copy code
            ScaffoldMessenger.of(context).showSnackBar(SnackBar(
              content: Text('تم نسخ الكوبون: ${coupon.code}'),
              duration: const Duration(seconds: 2)));
          },
          child: const Icon(Icons.copy, color: AppTheme.textSec, size: 20)),
      ),
    ]),
  );
}
