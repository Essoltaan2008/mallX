import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../../core/theme/app_theme.dart';
import '../../providers/providers.dart';

// ══════════════════════════════════════════════════════════════════════════
//  CHECKOUT SCREEN
// ══════════════════════════════════════════════════════════════════════════
class CheckoutScreen extends StatefulWidget {
  const CheckoutScreen({super.key});
  @override State<CheckoutScreen> createState() => _CheckoutScreenState();
}

class _CheckoutScreenState extends State<CheckoutScreen> {
  String _fulfillment = 'Delivery';
  String _payment     = 'Cash';
  final _addressCtrl  = TextEditingController();
  final _notesCtrl    = TextEditingController();
  bool _processing    = false;
  MallOrderDto? _confirmedOrder;

  @override
  void dispose() {
    _addressCtrl.dispose(); _notesCtrl.dispose(); super.dispose();
  }

  Future<void> _placeOrder() async {
    if (_fulfillment == 'Delivery' && _addressCtrl.text.trim().isEmpty) {
      _show('أدخل عنوان التوصيل أولاً');
      return;
    }
    setState(() => _processing = true);
    final cart  = context.read<CartProvider>();
    final error = await cart.checkout(
      fulfillmentType: _fulfillment,
      paymentMethod:   _payment,
      deliveryAddress: _fulfillment == 'Delivery' ? _addressCtrl.text.trim() : null,
      notes:           _notesCtrl.text.trim().isEmpty ? null : _notesCtrl.text.trim(),
    );
    setState(() => _processing = false);

    if (!mounted) return;
    if (error != null) { _show(error); return; }

    // Load latest order
    await context.read<MallProvider>().loadOrders();
    final orders = context.read<MallProvider>().orders;
    if (orders.isNotEmpty && mounted) {
      Navigator.pushReplacement(context,
        MaterialPageRoute(builder: (_) => OrderTrackingScreen(order: orders.first)));
    }
  }

  void _show(String msg) => ScaffoldMessenger.of(context).showSnackBar(
    SnackBar(content: Text(msg), backgroundColor: AppTheme.error));

  @override
  Widget build(BuildContext context) {
    final cart = context.watch<CartProvider>().cart;
    return Scaffold(
      appBar: AppBar(title: const Text('إتمام الطلب')),
      body: SingleChildScrollView(
        padding: const EdgeInsets.all(20),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [

          // ── Fulfillment ──────────────────────────────────────────────
          _sectionTitle('🚗 طريقة الاستلام'),
          const SizedBox(height: 10),
          Row(children: ['Delivery','Pickup','InStore'].map((type) {
            final labels = {'Delivery':'توصيل','Pickup':'استلام','InStore':'داخل المول'};
            final icons  = {'Delivery':Icons.delivery_dining,'Pickup':Icons.store,'InStore':Icons.location_on};
            final sel    = type == _fulfillment;
            return Expanded(child: GestureDetector(
              onTap: () => setState(() => _fulfillment = type),
              child: Container(
                margin: const EdgeInsets.only(left: 8),
                padding: const EdgeInsets.symmetric(vertical: 14),
                decoration: BoxDecoration(
                  color: sel ? AppTheme.primary.withOpacity(0.15) : AppTheme.card,
                  borderRadius: BorderRadius.circular(12),
                  border: Border.all(color: sel ? AppTheme.primary : AppTheme.border, width: sel ? 2 : 1),
                ),
                child: Column(children: [
                  Icon(icons[type], color: sel ? AppTheme.primary : AppTheme.textSec, size: 22),
                  const SizedBox(height: 6),
                  Text(labels[type]!, style: TextStyle(
                    color: sel ? AppTheme.primary : AppTheme.textSec,
                    fontSize: 12, fontWeight: sel ? FontWeight.w700 : FontWeight.normal)),
                ]),
              ),
            ));
          }).toList()),

          // ── Address (if Delivery) ─────────────────────────────────────
          if (_fulfillment == 'Delivery') ...[
            const SizedBox(height: 20),
            _sectionTitle('📍 عنوان التوصيل'),
            const SizedBox(height: 10),
            TextField(
              controller: _addressCtrl,
              maxLines: 2,
              decoration: const InputDecoration(
                hintText: 'أدخل العنوان بالتفصيل...',
                prefixIcon: Icon(Icons.location_on_outlined, color: AppTheme.textSec),
              ),
            ),
          ],

          // ── Payment ──────────────────────────────────────────────────
          const SizedBox(height: 20),
          _sectionTitle('💳 طريقة الدفع'),
          const SizedBox(height: 10),
          ...[
            ('Cash',       'نقدي عند الاستلام',    Icons.money),
            ('Paymob',     'بطاقة ائتمان (Paymob)', Icons.credit_card),
            ('Fawry',      'فوري',                  Icons.payment),
            ('VodafoneCash','فودافون كاش',           Icons.phone_android),
          ].map(((String, String, IconData) m) {
            final sel = m.$1 == _payment;
            return GestureDetector(
              onTap: () => setState(() => _payment = m.$1),
              child: Container(
                margin: const EdgeInsets.only(bottom: 8),
                padding: const EdgeInsets.all(14),
                decoration: BoxDecoration(
                  color: sel ? AppTheme.primary.withOpacity(0.1) : AppTheme.card,
                  borderRadius: BorderRadius.circular(12),
                  border: Border.all(color: sel ? AppTheme.primary : AppTheme.border),
                ),
                child: Row(children: [
                  Icon(m.$3, color: sel ? AppTheme.primary : AppTheme.textSec, size: 20),
                  const SizedBox(width: 12),
                  Expanded(child: Text(m.$2,
                    style: TextStyle(color: sel ? AppTheme.primary : AppTheme.textPri,
                      fontWeight: sel ? FontWeight.w700 : FontWeight.normal))),
                  if (sel) const Icon(Icons.check_circle, color: AppTheme.primary, size: 18),
                ]),
              ),
            );
          }),

          // ── Notes ────────────────────────────────────────────────────
          const SizedBox(height: 20),
          _sectionTitle('📝 ملاحظات (اختياري)'),
          const SizedBox(height: 10),
          TextField(
            controller: _notesCtrl,
            decoration: const InputDecoration(hintText: 'أي طلبات خاصة...'),
          ),

          // ── Summary ──────────────────────────────────────────────────
          const SizedBox(height: 24),
          Container(
            padding: const EdgeInsets.all(16),
            decoration: BoxDecoration(
              color: AppTheme.card, borderRadius: BorderRadius.circular(14),
              border: Border.all(color: AppTheme.border)),
            child: Column(children: [
              _summaryRow('المجموع الفرعي', '${cart.subtotal.toStringAsFixed(0)} ج.م'),
              const SizedBox(height: 6),
              _summaryRow('رسوم التوصيل',
                _fulfillment == 'Delivery' ? '${cart.deliveryFee.toStringAsFixed(0)} ج.م' : 'مجاني',
                valueColor: _fulfillment == 'Delivery' ? null : AppTheme.secondary),
              const Divider(color: AppTheme.border, height: 20),
              _summaryRow('الإجمالي', '${cart.total.toStringAsFixed(0)} ج.م',
                isBold: true, valueColor: AppTheme.primary),
            ]),
          ),
          const SizedBox(height: 24),

          // ── Place Order ──────────────────────────────────────────────
          ElevatedButton(
            onPressed: _processing ? null : _placeOrder,
            child: _processing
                ? const SizedBox(width: 20, height: 20,
                    child: CircularProgressIndicator(color: Colors.white, strokeWidth: 2))
                : const Text('تأكيد الطلب'),
          ),
          const SizedBox(height: 8),
        ]),
      ),
    );
  }

  Widget _sectionTitle(String t) => Text(t,
    style: const TextStyle(color: AppTheme.textPri, fontWeight: FontWeight.w700, fontSize: 15));

  Widget _summaryRow(String label, String value,
      {bool isBold = false, Color? valueColor}) =>
    Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
      Text(label, style: TextStyle(color: AppTheme.textSec,
        fontWeight: isBold ? FontWeight.w700 : FontWeight.normal)),
      Text(value, style: TextStyle(
        color: valueColor ?? AppTheme.textPri,
        fontWeight: isBold ? FontWeight.w800 : FontWeight.w600,
        fontSize: isBold ? 18 : 14)),
    ]);
}

// ══════════════════════════════════════════════════════════════════════════
//  ORDER TRACKING SCREEN
// ══════════════════════════════════════════════════════════════════════════
class OrderTrackingScreen extends StatefulWidget {
  final MallOrderDto order;
  const OrderTrackingScreen({super.key, required this.order});
  @override State<OrderTrackingScreen> createState() => _OrderTrackingScreenState();
}

class _OrderTrackingScreenState extends State<OrderTrackingScreen> {
  late MallOrderDto _order;

  @override
  void initState() {
    super.initState();
    _order = widget.order;
  }

  static const _steps = [
    ('Placed',    'تم الاستلام',    Icons.check_circle_outline),
    ('Confirmed', 'مؤكد',           Icons.thumb_up_outlined),
    ('Preparing', 'قيد التحضير',    Icons.restaurant_outlined),
    ('Ready',     'جاهز',           Icons.done_all),
    ('PickedUp',  'في الطريق',      Icons.delivery_dining),
    ('Delivered', 'تم التسليم',     Icons.home_outlined),
  ];

  int _currentStep() {
    final idx = _steps.indexWhere((s) => s.$1 == _order.status);
    return idx < 0 ? 0 : idx;
  }

  @override
  Widget build(BuildContext context) {
    final step = _currentStep();
    final isDone = _order.status == 'Delivered';

    return Scaffold(
      appBar: AppBar(
        title: Text(_order.orderNumber),
        leading: IconButton(
          icon: const Icon(Icons.arrow_back),
          onPressed: () => Navigator.pop(context),
        ),
      ),
      body: SingleChildScrollView(
        padding: const EdgeInsets.all(20),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [

          // ── Status banner ────────────────────────────────────────────
          Container(
            width: double.infinity,
            padding: const EdgeInsets.all(20),
            decoration: BoxDecoration(
              gradient: LinearGradient(
                colors: isDone
                  ? [const Color(0xFF065f46), const Color(0xFF064e3b)]
                  : [const Color(0xFF1e3a5f), const Color(0xFF1a0f3d)],
                begin: Alignment.topLeft, end: Alignment.bottomRight,
              ),
              borderRadius: BorderRadius.circular(18),
            ),
            child: Column(children: [
              Icon(isDone ? Icons.check_circle : Icons.pending_actions,
                color: isDone ? AppTheme.secondary : AppTheme.primary, size: 48),
              const SizedBox(height: 12),
              Text(
                isDone ? 'تم تسليم طلبك 🎉' : 'طلبك في الطريق إليك...',
                style: const TextStyle(color: Colors.white, fontSize: 18,
                  fontWeight: FontWeight.w800)),
              const SizedBox(height: 6),
              Text('${_order.total.toStringAsFixed(0)} ج.م — ${_order.paymentMethod}',
                style: const TextStyle(color: Colors.white70, fontSize: 14)),
            ]),
          ),

          const SizedBox(height: 28),

          // ── Timeline ────────────────────────────────────────────────
          const Text('مراحل الطلب',
            style: TextStyle(fontWeight: FontWeight.w700, color: AppTheme.textPri, fontSize: 16)),
          const SizedBox(height: 16),
          ..._steps.asMap().entries.map((e) {
            final i = e.key;
            final (status, label, icon) = e.value;
            final done    = i <= step;
            final current = i == step;
            return Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Column(children: [
                Container(
                  width: 36, height: 36,
                  decoration: BoxDecoration(
                    color: done ? AppTheme.primary : AppTheme.card,
                    shape: BoxShape.circle,
                    border: Border.all(
                      color: done ? AppTheme.primary : AppTheme.border, width: 2),
                  ),
                  child: Icon(icon,
                    color: done ? Colors.white : AppTheme.textSec, size: 18),
                ),
                if (i < _steps.length - 1)
                  Container(width: 2, height: 32,
                    color: i < step ? AppTheme.primary : AppTheme.border),
              ]),
              const SizedBox(width: 14),
              Expanded(child: Padding(
                padding: const EdgeInsets.only(top: 6, bottom: 32),
                child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  Row(children: [
                    Text(label, style: TextStyle(
                      color: current ? AppTheme.primary
                           : done ? AppTheme.textPri : AppTheme.textSec,
                      fontWeight: current || done ? FontWeight.w700 : FontWeight.normal,
                      fontSize: 14)),
                    if (current) ...[
                      const SizedBox(width: 8),
                      Container(padding: const EdgeInsets.symmetric(horizontal:8, vertical:2),
                        decoration: BoxDecoration(
                          color: AppTheme.primary.withOpacity(0.15),
                          borderRadius: BorderRadius.circular(8)),
                        child: const Text('الآن', style: TextStyle(
                          color: AppTheme.primary, fontSize: 11, fontWeight: FontWeight.w700))),
                    ],
                  ]),
                  // Show time from timeline
                  ...(_order.timeline
                    .where((t) => t.newStatus == status)
                    .map((t) => Text(
                      '${t.createdAt.hour}:${t.createdAt.minute.toString().padLeft(2,'0')}',
                      style: const TextStyle(color: AppTheme.textSec, fontSize: 11)))),
                ]),
              )),
            ]);
          }),

          const SizedBox(height: 8),

          // ── Store Orders ─────────────────────────────────────────────
          const Text('المحلات في طلبك',
            style: TextStyle(fontWeight: FontWeight.w700, color: AppTheme.textPri, fontSize: 16)),
          const SizedBox(height: 12),
          ..._order.storeOrders.map((so) => Container(
            margin: const EdgeInsets.only(bottom: 10),
            padding: const EdgeInsets.all(14),
            decoration: BoxDecoration(
              color: AppTheme.card, borderRadius: BorderRadius.circular(12),
              border: Border.all(color: AppTheme.border)),
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
                Text(so.storeName, style: const TextStyle(
                  fontWeight: FontWeight.w700, color: AppTheme.textPri)),
                _statusChip(so.status),
              ]),
              const Divider(color: AppTheme.border, height: 16),
              ...so.items.map((item) => Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
                  Text('${item.quantity}× ${item.productName}',
                    style: const TextStyle(color: AppTheme.textSec, fontSize: 13)),
                  Text('${item.total.toStringAsFixed(0)} ج.م',
                    style: const TextStyle(color: AppTheme.textPri, fontSize: 13)),
                ])),
            ]),
          )),
        ]),
      ),
    );
  }

  Widget _statusChip(String status) {
    final cfg = {
      'Placed':    ('#f59e0b', 'في الانتظار'),
      'Confirmed': ('#3b82f6', 'مؤكد'),
      'Preparing': ('#8b5cf6', 'قيد التحضير'),
      'Ready':     ('#10b981', 'جاهز'),
      'Cancelled': ('#ef4444', 'ملغى'),
    };
    final entry = cfg[status];
    if (entry == null) return const SizedBox();
    final color = Color(int.parse(entry.$1.replaceAll('#','FF'), radix:16) | 0xFF000000);
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 3),
      decoration: BoxDecoration(
        color: color.withOpacity(0.15), borderRadius: BorderRadius.circular(10)),
      child: Text(entry.$2,
        style: TextStyle(color: color, fontSize: 11, fontWeight: FontWeight.w700)),
    );
  }
}
