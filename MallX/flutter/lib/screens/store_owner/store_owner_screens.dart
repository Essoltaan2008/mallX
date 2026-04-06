import 'dart:async';
import 'package:flutter/material.dart';
import '../../core/theme/app_theme.dart';
import '../../data/services/api_service.dart';

// ══════════════════════════════════════════════════════════════════════════
//  STORE OWNER APP — Main Nav
// ══════════════════════════════════════════════════════════════════════════
class StoreOwnerApp extends StatefulWidget {
  const StoreOwnerApp({super.key});
  @override State<StoreOwnerApp> createState() => _StoreOwnerAppState();
}

class _StoreOwnerAppState extends State<StoreOwnerApp> {
  int _idx = 0;
  final _pages = const [
    StoreOrdersScreen(),
    RestaurantQueueScreen(),
    StoreBookingScheduleScreen(),
    StoreAnalyticsScreen(),
  ];

  @override
  Widget build(BuildContext context) => Scaffold(
    body: IndexedStack(index: _idx, children: _pages),
    bottomNavigationBar: BottomNavigationBar(
      currentIndex: _idx,
      onTap: (i) => setState(() => _idx = i),
      items: const [
        BottomNavigationBarItem(icon: Icon(Icons.receipt_long_outlined), activeIcon: Icon(Icons.receipt_long), label: 'الطلبات'),
        BottomNavigationBarItem(icon: Icon(Icons.queue_outlined),       activeIcon: Icon(Icons.queue),          label: 'الطابور'),
        BottomNavigationBarItem(icon: Icon(Icons.calendar_today_outlined),activeIcon: Icon(Icons.calendar_today),label: 'الحجوزات'),
        BottomNavigationBarItem(icon: Icon(Icons.bar_chart_outlined),   activeIcon: Icon(Icons.bar_chart),      label: 'التقارير'),
      ],
    ),
  );
}

// ══════════════════════════════════════════════════════════════════════════
//  STORE ORDERS SCREEN (Flutter version of Next.js store/dashboard)
// ══════════════════════════════════════════════════════════════════════════
class StoreOrdersScreen extends StatefulWidget {
  const StoreOrdersScreen({super.key});
  @override State<StoreOrdersScreen> createState() => _StoreOrdersScreenState();
}

class _StoreOrdersScreenState extends State<StoreOrdersScreen> {
  final _api    = ApiService();
  List<Map> _orders = [];
  bool _loading = true;
  String _filter = 'all';
  Timer? _timer;

  @override
  void initState() {
    super.initState();
    _load();
    _timer = Timer.periodic(const Duration(seconds: 30), (_) => _load());
  }

  @override void dispose() { _timer?.cancel(); super.dispose(); }

  Future<void> _load() async {
    try {
      final res = await _api.get('/mall/store/orders/incoming');
      setState(() { _orders = List<Map>.from(res.data['data'] ?? []); _loading = false; });
    } catch (_) { setState(() => _loading = false); }
  }

  Future<void> _updateStatus(String orderId, String status) async {
    await _api.patch('/mall/store/orders/$orderId/status',
      data: {'status': status, 'note': 'تحديث من التطبيق'});
    _load();
  }

  List<Map> get _filtered => _filter == 'all'
    ? _orders
    : _orders.where((o) => o['status'] == _filter).toList();

  static const _statusLabels = {
    'Placed': 'جديد', 'Confirmed': 'مؤكد',
    'Preparing': 'يتحضر', 'Ready': 'جاهز', 'Cancelled': 'ملغى',
  };
  static const _statusNext = {
    'Placed': 'Confirmed', 'Confirmed': 'Preparing', 'Preparing': 'Ready',
  };
  static const _statusColors = {
    'Placed': Color(0xFFF59E0B), 'Confirmed': Color(0xFF3B82F6),
    'Preparing': Color(0xFF8B5CF6), 'Ready': Color(0xFF10B981),
    'Cancelled': Color(0xFFEF4444),
  };

  @override
  Widget build(BuildContext context) {
    final newCount = _orders.where((o) => o['status'] == 'Placed').length;
    return Scaffold(
      appBar: AppBar(
        title: Row(children: [
          const Text('الطلبات الواردة'),
          if (newCount > 0) ...[
            const SizedBox(width: 10),
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
              decoration: BoxDecoration(color: AppTheme.error, borderRadius: BorderRadius.circular(10)),
              child: Text('$newCount', style: const TextStyle(color: Colors.white, fontSize: 12, fontWeight: FontWeight.w800))),
          ],
        ]),
        actions: [
          IconButton(icon: const Icon(Icons.refresh), onPressed: _load),
        ],
      ),
      body: Column(children: [
        // Filter chips
        SizedBox(height: 48, child: ListView(
          scrollDirection: Axis.horizontal, padding: const EdgeInsets.symmetric(horizontal: 12),
          children: [
            ...['all', 'Placed', 'Confirmed', 'Preparing', 'Ready'].map((s) {
              final count = s == 'all' ? _orders.length : _orders.where((o) => o['status'] == s).length;
              final sel   = _filter == s;
              return Padding(padding: const EdgeInsets.only(left: 8, top: 8),
                child: GestureDetector(
                  onTap: () => setState(() => _filter = s),
                  child: Container(
                    padding: const EdgeInsets.symmetric(horizontal: 14),
                    decoration: BoxDecoration(
                      color: sel ? AppTheme.primary : AppTheme.card,
                      borderRadius: BorderRadius.circular(20),
                      border: Border.all(color: sel ? AppTheme.primary : AppTheme.border)),
                    child: Center(child: Text(
                      s == 'all' ? 'الكل ($count)' : '${_statusLabels[s]} ($count)',
                      style: TextStyle(color: sel ? Colors.white : AppTheme.textSec, fontSize: 12, fontWeight: FontWeight.w600))))));
            }),
          ])),

        if (_loading) const Expanded(child: Center(child: CircularProgressIndicator()))
        else if (_filtered.isEmpty) const Expanded(child: Center(child: Text('لا توجد طلبات', style: TextStyle(color: AppTheme.textSec))))
        else Expanded(child: RefreshIndicator(
          onRefresh: _load,
          child: ListView.builder(
            padding: const EdgeInsets.all(12),
            itemCount: _filtered.length,
            itemBuilder: (_, i) {
              final o    = _filtered[i];
              final status = o['status'] as String? ?? '';
              final color  = _statusColors[status] ?? AppTheme.textSec;
              final nextStatus = _statusNext[status];
              return Container(
                margin: const EdgeInsets.only(bottom: 10),
                decoration: BoxDecoration(
                  color: AppTheme.card, borderRadius: BorderRadius.circular(14),
                  border: Border.all(color: AppTheme.border),
                  borderLeft: Border(right: BorderSide(color: color, width: 4))),
                child: Padding(
                  padding: const EdgeInsets.all(14),
                  child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                    Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
                      Text(o['orderNumber'] ?? '', style: const TextStyle(color: AppTheme.textPri, fontWeight: FontWeight.w800, fontSize: 15)),
                      Container(padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 3),
                        decoration: BoxDecoration(color: color.withOpacity(0.15), borderRadius: BorderRadius.circular(8)),
                        child: Text(_statusLabels[status] ?? status, style: TextStyle(color: color, fontSize: 11, fontWeight: FontWeight.w700))),
                    ]),
                    const SizedBox(height: 6),
                    Text('👤 ${o['customerName'] ?? ''} ${o['customerPhone'] != null ? '— ${o['customerPhone']}' : ''}',
                      style: const TextStyle(color: AppTheme.textSec, fontSize: 12)),
                    Text('${o['fulfillmentType'] == 'Delivery' ? '🚗 توصيل' : '🏪 استلام'} — ${o['total']} ج.م',
                      style: const TextStyle(color: AppTheme.textSec, fontSize: 12)),
                    if ((o['items'] as List?)?.isNotEmpty == true) ...[
                      const SizedBox(height: 8),
                      ...(o['items'] as List).map((item) => Text(
                        '  • ${item['quantity']}× ${item['productName']}',
                        style: const TextStyle(color: AppTheme.textSec, fontSize: 12))),
                    ],
                    if (nextStatus != null) ...[
                      const SizedBox(height: 10),
                      SizedBox(width: double.infinity,
                        child: ElevatedButton(
                          onPressed: () => _updateStatus(o['id'], nextStatus),
                          style: ElevatedButton.styleFrom(backgroundColor: color,
                            padding: const EdgeInsets.symmetric(vertical: 10)),
                          child: Text(
                            nextStatus == 'Confirmed' ? '✓ قبول الطلب'
                            : nextStatus == 'Preparing' ? '👨‍🍳 بدء التحضير'
                            : '✅ جاهز للاستلام',
                            style: const TextStyle(color: Colors.white, fontWeight: FontWeight.w700)))),
                    ],
                  ]),
                ),
              );
            }))),
      ]),
    );
  }
}

// ══════════════════════════════════════════════════════════════════════════
//  RESTAURANT QUEUE SCREEN
// ══════════════════════════════════════════════════════════════════════════
class RestaurantQueueScreen extends StatefulWidget {
  const RestaurantQueueScreen({super.key});
  @override State<RestaurantQueueScreen> createState() => _RestaurantQueueScreenState();
}

class _RestaurantQueueScreenState extends State<RestaurantQueueScreen> {
  final _api  = ApiService();
  Map? _queue;
  bool _loading = true;
  Timer? _timer;

  @override
  void initState() { super.initState(); _load(); _timer = Timer.periodic(const Duration(seconds: 15), (_) => _load()); }
  @override void dispose() { _timer?.cancel(); super.dispose(); }

  Future<void> _load() async {
    try {
      final res = await _api.get('/mall/store/restaurant/queue');
      setState(() { _queue = res.data['data']; _loading = false; });
    } catch (_) { setState(() => _loading = false); }
  }

  Future<void> _advance(String ticketId) async {
    await _api.patch('/mall/store/restaurant/queue/$ticketId/advance');
    _load();
  }

  @override
  Widget build(BuildContext context) {
    final tickets = List<Map>.from(_queue?['tickets'] ?? []);
    return Scaffold(
      appBar: AppBar(
        title: Row(children: [
          const Text('طابور المطعم'),
          const SizedBox(width: 10),
          if (_queue != null) Container(
            padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
            decoration: BoxDecoration(color: AppTheme.primary.withOpacity(0.15), borderRadius: BorderRadius.circular(8)),
            child: Text('انتظار: ${_queue!['totalWaiting'] ?? 0}',
              style: const TextStyle(color: AppTheme.primary, fontSize: 12, fontWeight: FontWeight.w700))),
        ]),
        actions: [IconButton(icon: const Icon(Icons.refresh), onPressed: _load)],
      ),
      body: _loading ? const Center(child: CircularProgressIndicator())
        : Column(children: [
            // Stats row
            if (_queue != null)
              Padding(padding: const EdgeInsets.all(12),
                child: Row(children: [
                  _statTile('الرقم الحالي', '${_queue!['currentServing'] ?? 0}', AppTheme.primary),
                  const SizedBox(width: 10),
                  _statTile('في الانتظار', '${_queue!['totalWaiting'] ?? 0}', AppTheme.accent),
                  const SizedBox(width: 10),
                  _statTile('متوسط التحضير', '${_queue!['avgPrepTimeMins'] ?? 0} د', AppTheme.secondary),
                ].map((w) => Expanded(child: w)).toList())),

            Expanded(child: tickets.isEmpty
              ? const Center(child: Text('لا توجد تذاكر حالياً', style: TextStyle(color: AppTheme.textSec)))
              : ListView.builder(
                padding: const EdgeInsets.symmetric(horizontal: 12),
                itemCount: tickets.length,
                itemBuilder: (_, i) {
                  final t    = tickets[i];
                  final status = t['status'] as String? ?? '';
                  final statusColor = switch (status) {
                    'Waiting'   => AppTheme.accent,
                    'Preparing' => AppTheme.primary,
                    'Ready'     => AppTheme.secondary,
                    _ => AppTheme.textSec,
                  };
                  return Container(
                    margin: const EdgeInsets.only(bottom: 8),
                    padding: const EdgeInsets.all(14),
                    decoration: BoxDecoration(color: AppTheme.card, borderRadius: BorderRadius.circular(12), border: Border.all(color: AppTheme.border)),
                    child: Row(children: [
                      Container(width: 50, height: 50, decoration: BoxDecoration(color: statusColor.withOpacity(0.15), borderRadius: BorderRadius.circular(12)),
                        child: Center(child: Text('#${t['ticketNumber']}', style: TextStyle(color: statusColor, fontWeight: FontWeight.w900, fontSize: 16)))),
                      const SizedBox(width: 12),
                      Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                        Text(t['orderNumber'] ?? '', style: const TextStyle(color: AppTheme.textPri, fontWeight: FontWeight.w700)),
                        Text(t['statusAr'] ?? status, style: TextStyle(color: statusColor, fontSize: 12)),
                      ])),
                      if (status != 'Ready' && status != 'Collected')
                        GestureDetector(
                          onTap: () => _advance(t['id']),
                          child: Container(padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
                            decoration: BoxDecoration(color: statusColor.withOpacity(0.15), borderRadius: BorderRadius.circular(8)),
                            child: Text(status == 'Waiting' ? 'ابدأ' : 'جاهز',
                              style: TextStyle(color: statusColor, fontWeight: FontWeight.w700, fontSize: 13)))),
                    ]));
                })),
          ]),
    );
  }

  Widget _statTile(String label, String val, Color color) => Container(
    padding: const EdgeInsets.all(12),
    decoration: BoxDecoration(color: color.withOpacity(0.1), borderRadius: BorderRadius.circular(12), border: Border.all(color: color.withOpacity(0.3))),
    child: Column(children: [
      Text(val, style: TextStyle(color: color, fontWeight: FontWeight.w900, fontSize: 20)),
      const SizedBox(height: 4),
      Text(label, style: const TextStyle(color: AppTheme.textSec, fontSize: 10), textAlign: TextAlign.center),
    ]));
}

// ══════════════════════════════════════════════════════════════════════════
//  STORE BOOKING SCHEDULE SCREEN
// ══════════════════════════════════════════════════════════════════════════
class StoreBookingScheduleScreen extends StatefulWidget {
  const StoreBookingScheduleScreen({super.key});
  @override State<StoreBookingScheduleScreen> createState() => _StoreBookingScheduleScreenState();
}

class _StoreBookingScheduleScreenState extends State<StoreBookingScheduleScreen> {
  final _api      = ApiService();
  List<Map> _bookings = [];
  bool _loading   = true;
  DateTime _date  = DateTime.now();

  @override void initState() { super.initState(); _load(); }

  Future<void> _load() async {
    final d = '${_date.year}-${_date.month.toString().padLeft(2,'0')}-${_date.day.toString().padLeft(2,'0')}';
    try {
      final res = await _api.get('/mall/store/bookings?date=$d');
      setState(() { _bookings = List<Map>.from(res.data['data'] ?? []); _loading = false; });
    } catch (_) { setState(() => _loading = false); }
  }

  Future<void> _updateStatus(String id, String status) async {
    await _api.patch('/mall/store/bookings/$id/status', data: {'status': status});
    _load();
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('جدول الحجوزات'),
      bottom: PreferredSize(
        preferredSize: const Size.fromHeight(52),
        child: Padding(padding: const EdgeInsets.only(bottom: 8),
          child: Row(mainAxisAlignment: MainAxisAlignment.center, children: [
            IconButton(icon: const Icon(Icons.chevron_right), onPressed: () { setState(() => _date = _date.subtract(const Duration(days: 1))); _load(); }),
            GestureDetector(
              onTap: () async {
                final d = await showDatePicker(context: context, initialDate: _date, firstDate: DateTime(2024), lastDate: DateTime(2026));
                if (d != null) { setState(() => _date = d); _load(); }
              },
              child: Container(padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
                decoration: BoxDecoration(color: AppTheme.card, borderRadius: BorderRadius.circular(10), border: Border.all(color: AppTheme.border)),
                child: Text('${_date.day}/${_date.month}/${_date.year}', style: const TextStyle(color: AppTheme.textPri, fontWeight: FontWeight.w700)))),
            IconButton(icon: const Icon(Icons.chevron_left), onPressed: () { setState(() => _date = _date.add(const Duration(days: 1))); _load(); }),
          ]))),
    ),
    body: _loading ? const Center(child: CircularProgressIndicator())
      : _bookings.isEmpty ? const Center(child: Text('لا توجد حجوزات لهذا اليوم', style: TextStyle(color: AppTheme.textSec)))
      : ListView.builder(
        padding: const EdgeInsets.all(12),
        itemCount: _bookings.length,
        itemBuilder: (_, i) {
          final b = _bookings[i];
          final status = b['status'] as String? ?? '';
          final color  = switch(status) {
            'Confirmed'  => AppTheme.primary, 'InProgress' => AppTheme.accent,
            'Completed'  => AppTheme.secondary, 'Cancelled' => AppTheme.error,
            _ => AppTheme.textSec,
          };
          return Container(
            margin: const EdgeInsets.only(bottom: 8),
            padding: const EdgeInsets.all(14),
            decoration: BoxDecoration(color: AppTheme.card, borderRadius: BorderRadius.circular(12), border: Border.all(color: AppTheme.border)),
            child: Row(children: [
              Container(width: 52, alignment: Alignment.center,
                child: Column(children: [
                  Text(b['startTime'] ?? '', style: const TextStyle(color: AppTheme.textPri, fontWeight: FontWeight.w700, fontSize: 13)),
                  const SizedBox(height: 2),
                  Text(b['endTime'] ?? '', style: const TextStyle(color: AppTheme.textSec, fontSize: 11)),
                ])),
              const SizedBox(width: 12),
              Container(width: 2, height: 48, color: color, margin: const EdgeInsets.only(left: 12)),
              Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text(b['serviceName'] ?? '', style: const TextStyle(color: AppTheme.textPri, fontWeight: FontWeight.w700)),
                Text('${b['bookingRef'] ?? ''} — ${b['price'] ?? 0} ج.م', style: const TextStyle(color: AppTheme.textSec, fontSize: 12)),
                if (b['staffName'] != null) Text('موظف: ${b['staffName']}', style: const TextStyle(color: AppTheme.textSec, fontSize: 11)),
              ])),
              if (status == 'Confirmed')
                TextButton(onPressed: () => _updateStatus(b['id'], 'InProgress'),
                  child: const Text('ابدأ', style: TextStyle(color: AppTheme.primary)))
              else if (status == 'InProgress')
                TextButton(onPressed: () => _updateStatus(b['id'], 'Completed'),
                  child: const Text('أنهِ', style: TextStyle(color: AppTheme.secondary))),
            ]));
        }));
}

// ══════════════════════════════════════════════════════════════════════════
//  STORE ANALYTICS SCREEN
// ══════════════════════════════════════════════════════════════════════════
class StoreAnalyticsScreen extends StatefulWidget {
  const StoreAnalyticsScreen({super.key});
  @override State<StoreAnalyticsScreen> createState() => _StoreAnalyticsScreenState();
}

class _StoreAnalyticsScreenState extends State<StoreAnalyticsScreen> {
  final _api    = ApiService();
  Map? _data;
  bool _loading = true;
  String _period = 'month';

  @override void initState() { super.initState(); _load(); }

  Future<void> _load() async {
    setState(() => _loading = true);
    try {
      final res = await _api.get('/mall/store/analytics?period=$_period');
      setState(() { _data = res.data['data']; _loading = false; });
    } catch (_) { setState(() => _loading = false); }
  }

  @override
  Widget build(BuildContext context) {
    final revenue = _data?['revenue'] as Map?;
    final orders  = _data?['orders']  as Map?;
    return Scaffold(
      appBar: AppBar(title: Text('تقارير ${_data?['period'] ?? ''}')),
      body: _loading ? const Center(child: CircularProgressIndicator())
        : SingleChildScrollView(
          padding: const EdgeInsets.all(16),
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            // Period selector
            Row(children: ['today','week','month','quarter'].map((p) {
              final labels = {'today':'اليوم','week':'أسبوع','month':'شهر','quarter':'ربع'};
              final sel = _period == p;
              return Expanded(child: Padding(padding: const EdgeInsets.symmetric(horizontal: 3),
                child: GestureDetector(onTap: () { setState(() => _period = p); _load(); },
                  child: Container(padding: const EdgeInsets.symmetric(vertical: 8),
                    alignment: Alignment.center,
                    decoration: BoxDecoration(
                      color: sel ? AppTheme.primary : AppTheme.card,
                      borderRadius: BorderRadius.circular(8),
                      border: Border.all(color: sel ? AppTheme.primary : AppTheme.border)),
                    child: Text(labels[p]!, style: TextStyle(color: sel ? Colors.white : AppTheme.textSec, fontSize: 13, fontWeight: FontWeight.w600))))));
            }).toList()),
            const SizedBox(height: 16),

            // KPIs
            _kpiRow([
              ('💰 الإيرادات', '${revenue?['total'] ?? 0} ج.م', AppTheme.primary),
              ('📦 الطلبات', '${orders?['total'] ?? 0}', AppTheme.secondary),
            ]),
            const SizedBox(height: 10),
            _kpiRow([
              ('✅ الإتمام', '${orders?['successRate'] ?? 0}%', AppTheme.secondary),
              ('⭐ التقييم', '${(_data?['avgRating'] ?? 0).toStringAsFixed(1)} (${_data?['totalRatings'] ?? 0})', AppTheme.accent),
            ]),
            const SizedBox(height: 10),
            _kpiRow([
              ('💸 صافي المحل', '${_data?['netAfterCommission'] ?? 0} ج.م', AppTheme.primary),
              ('📊 العمولة', '${_data?['commissionPaid'] ?? 0} ج.م', AppTheme.error),
            ]),

            const SizedBox(height: 20),

            // Top products
            if ((_data?['topProducts'] as List?)?.isNotEmpty == true) ...[
              const Text('🏆 أكثر المنتجات مبيعاً', style: TextStyle(color: AppTheme.textPri, fontWeight: FontWeight.w700, fontSize: 15)),
              const SizedBox(height: 10),
              ...(_data!['topProducts'] as List).take(5).map((p) => Container(
                margin: const EdgeInsets.only(bottom: 6),
                padding: const EdgeInsets.all(12),
                decoration: BoxDecoration(color: AppTheme.card, borderRadius: BorderRadius.circular(10), border: Border.all(color: AppTheme.border)),
                child: Row(children: [
                  Container(width: 28, height: 28, decoration: BoxDecoration(color: AppTheme.primary.withOpacity(0.15), borderRadius: BorderRadius.circular(8)),
                    child: Center(child: Text('#${p['rank']}', style: const TextStyle(color: AppTheme.primary, fontSize: 11, fontWeight: FontWeight.w800)))),
                  const SizedBox(width: 10),
                  Expanded(child: Text(p['productName'] ?? '', style: const TextStyle(color: AppTheme.textPri, fontSize: 13))),
                  Text('${p['quantity']} وحدة', style: const TextStyle(color: AppTheme.textSec, fontSize: 12)),
                  const SizedBox(width: 10),
                  Text('${p['revenue']} ج.م', style: const TextStyle(color: AppTheme.primary, fontWeight: FontWeight.w700, fontSize: 13)),
                ]))),
            ],
          ]));
  }

  Widget _kpiRow(List<(String, String, Color)> items) => Row(
    children: items.map((item) => Expanded(child: Padding(
      padding: const EdgeInsets.symmetric(horizontal: 3),
      child: Container(padding: const EdgeInsets.all(14),
        decoration: BoxDecoration(color: item.$3.withOpacity(0.08), borderRadius: BorderRadius.circular(12), border: Border.all(color: item.$3.withOpacity(0.25))),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Text(item.$1, style: const TextStyle(color: AppTheme.textSec, fontSize: 11)),
          const SizedBox(height: 6),
          Text(item.$2, style: TextStyle(color: item.$3, fontWeight: FontWeight.w800, fontSize: 16)),
        ]))))).toList());
}
