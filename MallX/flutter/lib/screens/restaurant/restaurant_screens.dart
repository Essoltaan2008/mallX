import 'package:flutter/material.dart';
import 'package:cached_network_image/cached_network_image.dart';
import '../../core/theme/app_theme.dart';
import '../../data/services/api_service.dart';
import '../../providers/providers.dart';
import 'package:provider/provider.dart';

// ══════════════════════════════════════════════════════════════════════════
//  DATA MODELS
// ══════════════════════════════════════════════════════════════════════════
class MenuCategoryModel {
  final String id, name;
  final String? nameAr, icon;
  final List<MenuItemModel> items;
  MenuCategoryModel({required this.id, required this.name,
    this.nameAr, this.icon, required this.items});
  factory MenuCategoryModel.fromJson(Map j) => MenuCategoryModel(
    id: j['id'] ?? '', name: j['name'], nameAr: j['nameAr'], icon: j['icon'],
    items: (j['items'] as List? ?? []).map((i) => MenuItemModel.fromJson(i)).toList());
}

class MenuItemModel {
  final String id, name;
  final String? nameAr, description, imageUrl;
  final double price;
  final int prepTimeMin;
  final bool isAvailable, isFeatured;
  final List<String> tags;
  MenuItemModel({required this.id, required this.name, this.nameAr,
    this.description, this.imageUrl, required this.price,
    required this.prepTimeMin, required this.isAvailable,
    required this.isFeatured, required this.tags});
  factory MenuItemModel.fromJson(Map j) => MenuItemModel(
    id: j['id'], name: j['name'], nameAr: j['nameAr'],
    description: j['description'], imageUrl: j['imageUrl'],
    price: (j['price'] as num).toDouble(), prepTimeMin: j['prepTimeMin'] ?? 15,
    isAvailable: j['isAvailable'] ?? true, isFeatured: j['isFeatured'] ?? false,
    tags: List<String>.from(j['tags'] ?? []));
}

class QueueTicketModel {
  final String id, status, statusAr, orderNumber;
  final int ticketNumber, waitingAhead;
  final DateTime? estimatedReady;
  QueueTicketModel({required this.id, required this.status, required this.statusAr,
    required this.orderNumber, required this.ticketNumber,
    required this.waitingAhead, this.estimatedReady});
  factory QueueTicketModel.fromJson(Map j) => QueueTicketModel(
    id: j['id'], status: j['status'], statusAr: j['statusAr'],
    orderNumber: j['orderNumber'] ?? '', ticketNumber: j['ticketNumber'],
    waitingAhead: j['waitingAhead'] ?? 0,
    estimatedReady: j['estimatedReady'] != null
        ? DateTime.parse(j['estimatedReady']) : null);
}

// ══════════════════════════════════════════════════════════════════════════
//  RESTAURANT MENU SCREEN
// ══════════════════════════════════════════════════════════════════════════
class RestaurantMenuScreen extends StatefulWidget {
  final String storeId, storeName;
  const RestaurantMenuScreen({super.key, required this.storeId, required this.storeName});
  @override State<RestaurantMenuScreen> createState() => _RestaurantMenuScreenState();
}

class _RestaurantMenuScreenState extends State<RestaurantMenuScreen> {
  final _api = ApiService();
  List<MenuCategoryModel> _categories = [];
  String? _selectedCat;
  bool _loading = true;
  String _search = '';

  @override
  void initState() { super.initState(); _loadMenu(); }

  Future<void> _loadMenu() async {
    try {
      final res = await _api.get('/mall/stores/${widget.storeId}/menu');
      final list = res.data['data'] as List? ?? [];
      setState(() {
        _categories = list.map((c) => MenuCategoryModel.fromJson(c)).toList();
        if (_categories.isNotEmpty) _selectedCat = _categories.first.id;
        _loading = false;
      });
    } catch (_) { setState(() => _loading = false); }
  }

  List<MenuItemModel> get _visibleItems {
    final cat = _categories.firstWhere((c) => c.id == _selectedCat,
        orElse: () => _categories.first);
    if (_search.isEmpty) return cat.items;
    return cat.items.where((i) =>
      i.name.contains(_search) || (i.nameAr?.contains(_search) ?? false)).toList();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: Text(widget.storeName)),
      body: _loading
        ? const Center(child: CircularProgressIndicator())
        : Column(children: [
            // Search
            Padding(
              padding: const EdgeInsets.all(12),
              child: TextField(
                onChanged: (v) => setState(() => _search = v),
                decoration: const InputDecoration(
                  hintText: 'ابحث في المنيو...', prefixIcon: Icon(Icons.search),
                ),
              ),
            ),
            // Category tabs
            if (_categories.isNotEmpty)
              SizedBox(height: 44,
                child: ListView.separated(
                  scrollDirection: Axis.horizontal,
                  padding: const EdgeInsets.symmetric(horizontal: 12),
                  itemCount: _categories.length,
                  separatorBuilder: (_, __) => const SizedBox(width: 8),
                  itemBuilder: (_, i) {
                    final cat = _categories[i];
                    final sel = cat.id == _selectedCat;
                    return GestureDetector(
                      onTap: () => setState(() { _selectedCat = cat.id; _search = ''; }),
                      child: Container(
                        padding: const EdgeInsets.symmetric(horizontal: 16),
                        alignment: Alignment.center,
                        decoration: BoxDecoration(
                          color: sel ? AppTheme.primary : AppTheme.card,
                          borderRadius: BorderRadius.circular(20),
                          border: Border.all(color: sel ? AppTheme.primary : AppTheme.border),
                        ),
                        child: Text(cat.nameAr ?? cat.name,
                          style: TextStyle(
                            color: sel ? Colors.white : AppTheme.textSec,
                            fontWeight: sel ? FontWeight.w700 : FontWeight.normal,
                            fontSize: 13)),
                      ),
                    );
                  },
                ),
              ),
            const SizedBox(height: 8),
            // Items
            Expanded(
              child: _visibleItems.isEmpty
                ? const Center(child: Text('لا توجد أصناف', style: TextStyle(color: AppTheme.textSec)))
                : ListView.separated(
                    padding: const EdgeInsets.all(12),
                    itemCount: _visibleItems.length,
                    separatorBuilder: (_, __) => const SizedBox(height: 10),
                    itemBuilder: (_, i) => _MenuItemCard(item: _visibleItems[i], storeId: widget.storeId),
                  ),
            ),
          ]),
    );
  }
}

class _MenuItemCard extends StatelessWidget {
  final MenuItemModel item;
  final String storeId;
  const _MenuItemCard({required this.item, required this.storeId});

  @override
  Widget build(BuildContext context) {
    return Container(
      decoration: BoxDecoration(
        color: AppTheme.card, borderRadius: BorderRadius.circular(14),
        border: Border.all(color: AppTheme.border),
      ),
      child: Row(children: [
        // Image
        ClipRRect(
          borderRadius: const BorderRadius.only(
            topRight: Radius.circular(14), bottomRight: Radius.circular(14)),
          child: item.imageUrl != null
            ? CachedNetworkImage(imageUrl: item.imageUrl!,
                width: 100, height: 100, fit: BoxFit.cover,
                placeholder: (_, __) => Container(
                  width: 100, height: 100, color: AppTheme.border))
            : Container(width: 100, height: 100, color: AppTheme.surface,
                child: const Icon(Icons.restaurant_menu, color: AppTheme.textSec, size: 36)),
        ),
        const SizedBox(width: 12),
        Expanded(
          child: Padding(
            padding: const EdgeInsets.symmetric(vertical: 12, horizontal: 4),
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Row(children: [
                Expanded(child: Text(item.nameAr ?? item.name,
                  style: const TextStyle(fontWeight: FontWeight.w700,
                    color: AppTheme.textPri, fontSize: 15))),
                if (item.isFeatured)
                  const Icon(Icons.star, color: Color(0xFFF59E0B), size: 16),
              ]),
              if (item.description != null) ...[
                const SizedBox(height: 4),
                Text(item.description!, maxLines: 2, overflow: TextOverflow.ellipsis,
                  style: const TextStyle(color: AppTheme.textSec, fontSize: 12)),
              ],
              const SizedBox(height: 6),
              Row(children: [
                Text('${item.price.toStringAsFixed(0)} ج.م',
                  style: const TextStyle(color: AppTheme.primary,
                    fontWeight: FontWeight.w800, fontSize: 15)),
                const SizedBox(width: 8),
                const Icon(Icons.timer_outlined, size: 12, color: AppTheme.textSec),
                const SizedBox(width: 2),
                Text('${item.prepTimeMin} د',
                  style: const TextStyle(color: AppTheme.textSec, fontSize: 11)),
                if (item.tags.isNotEmpty) ...[
                  const SizedBox(width: 8),
                  ...item.tags.take(2).map((t) => Container(
                    margin: const EdgeInsets.only(left: 4),
                    padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 1),
                    decoration: BoxDecoration(
                      color: AppTheme.primary.withOpacity(0.1),
                      borderRadius: BorderRadius.circular(6)),
                    child: Text(t, style: const TextStyle(
                      color: AppTheme.primary, fontSize: 9)))),
                ],
              ]),
            ]),
          ),
        ),
        // Add button
        Padding(
          padding: const EdgeInsets.all(12),
          child: GestureDetector(
            onTap: item.isAvailable ? () => _addToCart(context) : null,
            child: Container(
              width: 34, height: 34,
              decoration: BoxDecoration(
                color: item.isAvailable ? AppTheme.primary : AppTheme.border,
                borderRadius: BorderRadius.circular(10)),
              child: const Icon(Icons.add, color: Colors.white, size: 20),
            ),
          ),
        ),
      ]),
    );
  }

  Future<void> _addToCart(BuildContext context) async {
    final cart  = context.read<CartProvider>();
    final error = await cart.addItem(
      productId: item.id, storeId: storeId, quantity: 1);
    if (!context.mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(
      content: Text(error ?? '✅ تمت الإضافة للسلة: ${item.nameAr ?? item.name}'),
      backgroundColor: error != null ? AppTheme.error : AppTheme.secondary,
      duration: const Duration(seconds: 2),
    ));
  }
}

// ══════════════════════════════════════════════════════════════════════════
//  QUEUE TRACKER SCREEN
// ══════════════════════════════════════════════════════════════════════════
class QueueTrackerScreen extends StatefulWidget {
  final String ticketId;
  const QueueTrackerScreen({super.key, required this.ticketId});
  @override State<QueueTrackerScreen> createState() => _QueueTrackerScreenState();
}

class _QueueTrackerScreenState extends State<QueueTrackerScreen> {
  final _api = ApiService();
  QueueTicketModel? _ticket;
  bool _loading = true;

  @override
  void initState() {
    super.initState();
    _load();
    // Auto-refresh every 20s
    Future.doWhile(() async {
      await Future.delayed(const Duration(seconds: 20));
      if (!mounted) return false;
      await _load();
      return mounted;
    });
  }

  Future<void> _load() async {
    try {
      final res = await _api.get('/mall/stores/queue/${widget.ticketId}');
      if (mounted) setState(() {
        _ticket = QueueTicketModel.fromJson(res.data['data']);
        _loading = false;
      });
    } catch (_) { if (mounted) setState(() => _loading = false); }
  }

  Color _statusColor(String s) => switch (s) {
    'Waiting'   => const Color(0xFFF59E0B),
    'Preparing' => AppTheme.primary,
    'Ready'     => AppTheme.secondary,
    'Collected' => AppTheme.textSec,
    _ => AppTheme.textSec,
  };

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('تتبع طلبك')),
      body: _loading
        ? const Center(child: CircularProgressIndicator())
        : _ticket == null
          ? const Center(child: Text('تعذر تحميل بيانات الطلب'))
          : SingleChildScrollView(
              padding: const EdgeInsets.all(24),
              child: Column(children: [
                // Ticket number big display
                Container(
                  width: double.infinity,
                  padding: const EdgeInsets.symmetric(vertical: 40),
                  decoration: BoxDecoration(
                    gradient: LinearGradient(
                      colors: [_statusColor(_ticket!.status).withOpacity(0.2),
                        AppTheme.surface],
                      begin: Alignment.topCenter, end: Alignment.bottomCenter),
                    borderRadius: BorderRadius.circular(24),
                    border: Border.all(color: _statusColor(_ticket!.status).withOpacity(0.4)),
                  ),
                  child: Column(children: [
                    Text('رقم تذكرتك', style: const TextStyle(
                      color: AppTheme.textSec, fontSize: 14)),
                    const SizedBox(height: 8),
                    Text('#${_ticket!.ticketNumber}', style: TextStyle(
                      color: _statusColor(_ticket!.status),
                      fontSize: 72, fontWeight: FontWeight.w900)),
                    const SizedBox(height: 12),
                    Container(
                      padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 8),
                      decoration: BoxDecoration(
                        color: _statusColor(_ticket!.status).withOpacity(0.15),
                        borderRadius: BorderRadius.circular(20)),
                      child: Text(_ticket!.statusAr, style: TextStyle(
                        color: _statusColor(_ticket!.status),
                        fontWeight: FontWeight.w700, fontSize: 16)),
                    ),
                  ]),
                ),

                const SizedBox(height: 24),

                // Info cards
                Row(children: [
                  _infoCard('الانتظار قبلك', '${_ticket!.waitingAhead}', Icons.people_outline),
                  const SizedBox(width: 12),
                  _infoCard(
                    'الوقت المتوقع',
                    _ticket!.estimatedReady != null
                      ? '${_ticket!.estimatedReady!.hour}:${_ticket!.estimatedReady!.minute.toString().padLeft(2,"0")}'
                      : '—',
                    Icons.timer_outlined,
                  ),
                ].map((w) => Expanded(child: w)).toList()),

                const SizedBox(height: 24),

                // Status explanation
                if (_ticket!.status == 'Ready') ...[
                  Container(
                    padding: const EdgeInsets.all(20),
                    decoration: BoxDecoration(
                      color: AppTheme.secondary.withOpacity(0.1),
                      borderRadius: BorderRadius.circular(16),
                      border: Border.all(color: AppTheme.secondary.withOpacity(0.3)),
                    ),
                    child: const Row(children: [
                      Icon(Icons.notifications_active, color: AppTheme.secondary, size: 32),
                      SizedBox(width: 16),
                      Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                        Text('طلبك جاهز! 🎉', style: TextStyle(
                          color: AppTheme.secondary, fontWeight: FontWeight.w800, fontSize: 16)),
                        SizedBox(height: 4),
                        Text('توجه الآن لاستلام طلبك من المحل',
                          style: TextStyle(color: AppTheme.textSec, fontSize: 13)),
                      ])),
                    ]),
                  ),
                ] else ...[
                  Container(
                    padding: const EdgeInsets.all(16),
                    decoration: BoxDecoration(
                      color: AppTheme.card, borderRadius: BorderRadius.circular(14),
                      border: Border.all(color: AppTheme.border)),
                    child: Row(children: [
                      const Icon(Icons.info_outline, color: AppTheme.textSec),
                      const SizedBox(width: 12),
                      Expanded(child: Text(
                        _ticket!.status == 'Waiting'
                          ? 'طلبك في قائمة الانتظار. سنخطرك فور جهوزيته.'
                          : 'طلبك قيد التحضير الآن. يرجى الانتظار.',
                        style: const TextStyle(color: AppTheme.textSec, fontSize: 13))),
                    ]),
                  ),
                ],

                const SizedBox(height: 20),
                TextButton.icon(
                  onPressed: _load,
                  icon: const Icon(Icons.refresh, color: AppTheme.primary),
                  label: const Text('تحديث الحالة', style: TextStyle(color: AppTheme.primary)),
                ),
              ]),
            ),
    );
  }

  Widget _infoCard(String label, String value, IconData icon) =>
    Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: AppTheme.card, borderRadius: BorderRadius.circular(14),
        border: Border.all(color: AppTheme.border)),
      child: Column(children: [
        Icon(icon, color: AppTheme.textSec, size: 20),
        const SizedBox(height: 8),
        Text(value, style: const TextStyle(
          color: AppTheme.textPri, fontWeight: FontWeight.w800, fontSize: 24)),
        const SizedBox(height: 4),
        Text(label, style: const TextStyle(color: AppTheme.textSec, fontSize: 11)),
      ]),
    );
}
