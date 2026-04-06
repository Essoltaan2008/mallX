import 'package:flutter/material.dart';
import '../../core/theme/app_theme.dart';
import '../../data/services/api_service.dart';

// ══════════════════════════════════════════════════════════════════════════
//  SEARCH SCREEN
// ══════════════════════════════════════════════════════════════════════════
class SearchScreen extends StatefulWidget {
  final String mallId;
  const SearchScreen({super.key, required this.mallId});
  @override State<SearchScreen> createState() => _SearchScreenState();
}

class _SearchScreenState extends State<SearchScreen> {
  final _api     = ApiService();
  final _ctrl    = TextEditingController();
  final _focus   = FocusNode();
  bool  _loading = false;
  List<String> _trending   = [];
  List<Map>    _stores     = [];
  List<Map>    _products   = [];
  List<Map>    _menuItems  = [];
  List<String> _suggestions = [];
  String       _query      = '';

  @override
  void initState() {
    super.initState();
    _loadTrending();
    _focus.requestFocus();
  }

  @override
  void dispose() { _ctrl.dispose(); _focus.dispose(); super.dispose(); }

  Future<void> _loadTrending() async {
    try {
      final res = await _api.get('/mall/${widget.mallId}/search/trending');
      setState(() => _trending = List<String>.from(res.data['data'] ?? []));
    } catch (_) {}
  }

  Future<void> _search(String q) async {
    if (q.trim().length < 2) return;
    setState(() { _query = q.trim(); _loading = true; });
    try {
      final res = await _api.get('/mall/${widget.mallId}/search?q=${Uri.encodeComponent(q)}');
      final data = res.data['data'];
      setState(() {
        _stores     = List<Map>.from(data['stores']     ?? []);
        _products   = List<Map>.from(data['products']   ?? []);
        _menuItems  = List<Map>.from(data['menuItems']  ?? []);
        _suggestions= List<String>.from(data['suggestions'] ?? []);
      });
    } catch (_) {}
    setState(() => _loading = false);
  }

  @override
  Widget build(BuildContext context) {
    final hasResults = _stores.isNotEmpty || _products.isNotEmpty || _menuItems.isNotEmpty;

    return Scaffold(
      appBar: AppBar(
        titleSpacing: 0,
        title: TextField(
          controller: _ctrl,
          focusNode:  _focus,
          onChanged:  (v) { if (v.length > 1) _search(v); },
          onSubmitted: _search,
          decoration: const InputDecoration(
            hintText: 'ابحث عن محلات، منتجات، أكل...',
            border: InputBorder.none,
            contentPadding: EdgeInsets.symmetric(horizontal: 16, vertical: 12),
          ),
          style: const TextStyle(color: AppTheme.textPri, fontSize: 16),
        ),
        actions: [
          if (_ctrl.text.isNotEmpty)
            IconButton(
              icon: const Icon(Icons.clear),
              onPressed: () {
                _ctrl.clear();
                setState(() { _stores=[]; _products=[]; _menuItems=[]; _query=''; });
              }),
        ],
      ),
      body: _loading
        ? const Center(child: CircularProgressIndicator())
        : hasResults
          ? _ResultsView(
              stores: _stores, products: _products, menuItems: _menuItems,
              suggestions: _suggestions, onSuggestion: (s) {
                _ctrl.text = s;
                _search(s);
              })
          : _EmptySearchView(
              trending: _trending,
              onSelect: (q) { _ctrl.text = q; _search(q); }),
    );
  }
}

class _EmptySearchView extends StatelessWidget {
  final List<String> trending;
  final ValueChanged<String> onSelect;
  const _EmptySearchView({required this.trending, required this.onSelect});

  @override
  Widget build(BuildContext context) => SingleChildScrollView(
    padding: const EdgeInsets.all(16),
    child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
      if (trending.isNotEmpty) ...[
        const Row(children: [
          Icon(Icons.trending_up, color: AppTheme.accent, size: 18),
          SizedBox(width: 8),
          Text('الأكثر بحثاً', style: TextStyle(
            color: AppTheme.textPri, fontWeight: FontWeight.w700, fontSize: 15)),
        ]),
        const SizedBox(height: 12),
        Wrap(spacing: 8, runSpacing: 8, children: trending.map((t) =>
          GestureDetector(
            onTap: () => onSelect(t),
            child: Container(
              padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 8),
              decoration: BoxDecoration(
                color: AppTheme.card, borderRadius: BorderRadius.circular(20),
                border: Border.all(color: AppTheme.border)),
              child: Row(mainAxisSize: MainAxisSize.min, children: [
                const Icon(Icons.search, color: AppTheme.textSec, size: 14),
                const SizedBox(width: 6),
                Text(t, style: const TextStyle(color: AppTheme.textSec, fontSize: 13)),
              ]),
            ),
          )).toList()),
      ],
    ]),
  );
}

class _ResultsView extends StatelessWidget {
  final List<Map> stores, products, menuItems;
  final List<String> suggestions;
  final ValueChanged<String> onSuggestion;
  const _ResultsView({required this.stores, required this.products,
    required this.menuItems, required this.suggestions, required this.onSuggestion});

  @override
  Widget build(BuildContext context) => ListView(
    padding: const EdgeInsets.all(16),
    children: [
      // Suggestions
      if (suggestions.isNotEmpty) ...[
        Wrap(spacing: 8, children: suggestions.map((s) =>
          GestureDetector(
            onTap: () => onSuggestion(s),
            child: Container(
              padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
              decoration: BoxDecoration(color: AppTheme.card,
                borderRadius: BorderRadius.circular(12),
                border: Border.all(color: AppTheme.border)),
              child: Text(s, style: const TextStyle(color: AppTheme.textSec, fontSize: 12))))).toList()),
        const SizedBox(height: 16),
      ],
      // Stores
      if (stores.isNotEmpty) ...[
        _sectionHeader('🏪 المحلات', stores.length),
        ...stores.map((s) => _StoreResultCard(store: s)),
        const SizedBox(height: 16),
      ],
      // Products
      if (products.isNotEmpty) ...[
        _sectionHeader('📦 المنتجات', products.length),
        GridView.count(
          shrinkWrap: true, physics: const NeverScrollableScrollPhysics(),
          crossAxisCount: 2, crossAxisSpacing: 10, mainAxisSpacing: 10,
          childAspectRatio: 0.85,
          children: products.take(6).map((p) => _ProductResultCard(product: p)).toList()),
        const SizedBox(height: 16),
      ],
      // Menu items
      if (menuItems.isNotEmpty) ...[
        _sectionHeader('🍽️ من المنيو', menuItems.length),
        ...menuItems.map((m) => _MenuResultCard(item: m)),
      ],
    ]);

  Widget _sectionHeader(String title, int count) => Padding(
    padding: const EdgeInsets.only(bottom: 10),
    child: Row(children: [
      Text(title, style: const TextStyle(color: AppTheme.textPri, fontWeight: FontWeight.w700, fontSize: 15)),
      const SizedBox(width: 8),
      Text('($count)', style: const TextStyle(color: AppTheme.textSec, fontSize: 13)),
    ]));
}

class _StoreResultCard extends StatelessWidget {
  final Map store;
  const _StoreResultCard({required this.store});
  @override
  Widget build(BuildContext context) => Container(
    margin: const EdgeInsets.only(bottom: 8),
    padding: const EdgeInsets.all(14),
    decoration: BoxDecoration(color: AppTheme.card, borderRadius: BorderRadius.circular(12),
      border: Border.all(color: AppTheme.border)),
    child: Row(children: [
      Container(width: 46, height: 46, decoration: BoxDecoration(
        color: AppTheme.primary.withOpacity(0.1), borderRadius: BorderRadius.circular(12)),
        child: const Icon(Icons.storefront_outlined, color: AppTheme.primary)),
      const SizedBox(width: 12),
      Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Text(store['name'] ?? '', style: const TextStyle(color: AppTheme.textPri, fontWeight: FontWeight.w700)),
        Row(children: [
          Text(store['storeType'] ?? '', style: const TextStyle(color: AppTheme.textSec, fontSize: 11)),
          const SizedBox(width: 8),
          const Icon(Icons.star, color: Color(0xFFF59E0B), size: 12),
          Text(' ${(store['avgRating'] ?? 0).toStringAsFixed(1)}',
            style: const TextStyle(color: AppTheme.textSec, fontSize: 11)),
        ]),
      ])),
      const Icon(Icons.chevron_left, color: AppTheme.textSec),
    ]));
}

class _ProductResultCard extends StatelessWidget {
  final Map product;
  const _ProductResultCard({required this.product});
  @override
  Widget build(BuildContext context) => Container(
    decoration: BoxDecoration(color: AppTheme.card, borderRadius: BorderRadius.circular(12),
      border: Border.all(color: AppTheme.border)),
    child: Column(children: [
      Expanded(child: Container(
        decoration: BoxDecoration(color: AppTheme.surface,
          borderRadius: const BorderRadius.only(topRight: Radius.circular(12), topLeft: Radius.circular(12))),
        child: const Center(child: Icon(Icons.inventory_2_outlined, color: AppTheme.textSec, size: 32)))),
      Padding(padding: const EdgeInsets.all(10), child: Column(
        crossAxisAlignment: CrossAxisAlignment.start, children: [
          Text(product['name'] ?? '', style: const TextStyle(color: AppTheme.textPri, fontWeight: FontWeight.w600, fontSize: 13),
            maxLines: 2, overflow: TextOverflow.ellipsis),
          const SizedBox(height: 4),
          Text('${product['price']} ج.م', style: const TextStyle(color: AppTheme.primary, fontWeight: FontWeight.w800)),
        ])),
    ]));
}

class _MenuResultCard extends StatelessWidget {
  final Map item;
  const _MenuResultCard({required this.item});
  @override
  Widget build(BuildContext context) => Container(
    margin: const EdgeInsets.only(bottom: 8),
    padding: const EdgeInsets.all(12),
    decoration: BoxDecoration(color: AppTheme.card, borderRadius: BorderRadius.circular(12),
      border: Border.all(color: AppTheme.border)),
    child: Row(children: [
      const Icon(Icons.restaurant_menu, color: AppTheme.accent),
      const SizedBox(width: 12),
      Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Text(item['nameAr'] ?? item['name'] ?? '', style: const TextStyle(color: AppTheme.textPri, fontWeight: FontWeight.w600)),
        Text('${item['prepTimeMin']} د • ${item['price']} ج.م',
          style: const TextStyle(color: AppTheme.textSec, fontSize: 12)),
      ])),
    ]));
}

// ══════════════════════════════════════════════════════════════════════════
//  MALL MAP SCREEN (Interactive SVG)
// ══════════════════════════════════════════════════════════════════════════
class MallMapScreen extends StatefulWidget {
  final String mallId;
  const MallMapScreen({super.key, required this.mallId});
  @override State<MallMapScreen> createState() => _MallMapScreenState();
}

class _MallMapScreenState extends State<MallMapScreen> {
  final _api    = ApiService();
  Map?  _mapData;
  int   _floor  = 0;
  bool  _loading= true;
  Map?  _selectedStore;

  @override
  void initState() { super.initState(); _load(); }

  Future<void> _load() async {
    try {
      final res = await _api.get('/mall/${widget.mallId}/map');
      setState(() { _mapData = res.data['data']; _loading = false; });
    } catch (_) { setState(() => _loading = false); }
  }

  List<Map> get _floors => List<Map>.from(_mapData?['floors'] ?? []);
  Map get _currentFloor => _floors.isNotEmpty ? _floors[_floor] : {};
  List<Map> get _stores => List<Map>.from(_currentFloor['stores'] ?? []);
  List<Map> get _amenities => List<Map>.from(_currentFloor['amenities'] ?? []);

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: Text(_mapData?['name'] ?? 'خريطة المول')),
      body: _loading
        ? const Center(child: CircularProgressIndicator())
        : _mapData == null
          ? const Center(child: Text('تعذر تحميل الخريطة'))
          : Column(children: [

              // Floor selector
              if (_floors.length > 1)
                SizedBox(height: 46, child: ListView.separated(
                  scrollDirection: Axis.horizontal,
                  padding: const EdgeInsets.symmetric(horizontal: 12),
                  itemCount: _floors.length,
                  separatorBuilder: (_, __) => const SizedBox(width: 8),
                  itemBuilder: (_, i) {
                    final f   = _floors[i];
                    final sel = i == _floor;
                    return GestureDetector(
                      onTap: () => setState(() { _floor = i; _selectedStore = null; }),
                      child: Container(
                        padding: const EdgeInsets.symmetric(horizontal: 16),
                        alignment: Alignment.center,
                        decoration: BoxDecoration(
                          color: sel ? AppTheme.primary : AppTheme.card,
                          borderRadius: BorderRadius.circular(20),
                          border: Border.all(color: sel ? AppTheme.primary : AppTheme.border)),
                        child: Text(f['nameAr'] ?? f['name'] ?? '',
                          style: TextStyle(
                            color: sel ? Colors.white : AppTheme.textSec,
                            fontWeight: sel ? FontWeight.w700 : FontWeight.normal,
                            fontSize: 13))));
                  })),

              // Interactive Map
              Expanded(child: Stack(children: [
                InteractiveViewer(
                  boundaryMargin: const EdgeInsets.all(50),
                  minScale: 0.5, maxScale: 3,
                  child: Container(
                    margin: const EdgeInsets.all(16),
                    decoration: BoxDecoration(
                      color: AppTheme.surface, borderRadius: BorderRadius.circular(16),
                      border: Border.all(color: AppTheme.border)),
                    child: CustomPaint(
                      painter: _MapPainter(
                        stores: _stores, amenities: _amenities,
                        selectedId: _selectedStore?['storeId']),
                      child: GestureDetector(
                        onTapDown: (d) => _handleTap(d.localPosition),
                        child: const SizedBox(width: 600, height: 500))))),

                // Legend
                Positioned(bottom: 16, right: 16,
                  child: Container(
                    padding: const EdgeInsets.all(10),
                    decoration: BoxDecoration(color: AppTheme.card,
                      borderRadius: BorderRadius.circular(10),
                      border: Border.all(color: AppTheme.border)),
                    child: Column(crossAxisAlignment: CrossAxisAlignment.start,
                      mainAxisSize: MainAxisSize.min, children: [
                      _legendItem(AppTheme.primary, 'متجر'),
                      _legendItem(const Color(0xFFF59E0B), 'مطعم'),
                      _legendItem(AppTheme.secondary, 'خدمة'),
                      _legendItem(AppTheme.textSec, 'مرافق'),
                    ]))),
              ])),

              // Selected Store Detail
              if (_selectedStore != null)
                _StoreDetailPanel(
                  store: _selectedStore!,
                  onClose: () => setState(() => _selectedStore = null)),
            ]),
    );
  }

  void _handleTap(Offset pos) {
    // Find tapped store
    for (final store in _stores) {
      final x = (store['posX'] as num).toDouble();
      final y = (store['posY'] as num).toDouble();
      final w = (store['width'] as num?)?.toDouble() ?? 60;
      final h = (store['height'] as num?)?.toDouble() ?? 40;
      if (pos.dx >= x && pos.dx <= x + w && pos.dy >= y && pos.dy <= y + h) {
        setState(() => _selectedStore = store);
        return;
      }
    }
    setState(() => _selectedStore = null);
  }

  Widget _legendItem(Color color, String label) => Padding(
    padding: const EdgeInsets.only(bottom: 4),
    child: Row(children: [
      Container(width: 12, height: 12, decoration: BoxDecoration(color: color, borderRadius: BorderRadius.circular(3))),
      const SizedBox(width: 6),
      Text(label, style: const TextStyle(color: AppTheme.textSec, fontSize: 11)),
    ]));
}

class _MapPainter extends CustomPainter {
  final List<Map> stores, amenities;
  final String? selectedId;

  _MapPainter({required this.stores, required this.amenities, this.selectedId});

  @override
  void paint(Canvas canvas, Size size) {
    for (final store in stores) {
      final x = (store['posX'] as num).toDouble();
      final y = (store['posY'] as num).toDouble();
      final w = (store['width'] as num?)?.toDouble() ?? 60;
      final h = (store['height'] as num?)?.toDouble() ?? 40;
      final isSelected = store['storeId'] == selectedId;

      final color = _typeColor(store['storeType'] as String? ?? 'Retail');
      final paint = Paint()
        ..color = isSelected ? color : color.withOpacity(0.7)
        ..style = PaintingStyle.fill;
      final borderPaint = Paint()
        ..color = isSelected ? Colors.white : color
        ..style = PaintingStyle.stroke
        ..strokeWidth = isSelected ? 2 : 1;

      final rect = Rect.fromLTWH(x, y, w, h);
      canvas.drawRRect(RRect.fromRectAndRadius(rect, const Radius.circular(6)), paint);
      canvas.drawRRect(RRect.fromRectAndRadius(rect, const Radius.circular(6)), borderPaint);

      // Label
      final textPainter = TextPainter(
        text: TextSpan(
          text: (store['storeName'] as String? ?? '').split(' ').first,
          style: TextStyle(color: Colors.white, fontSize: isSelected ? 11 : 9,
            fontWeight: isSelected ? FontWeight.bold : FontWeight.normal)),
        textDirection: TextDirection.rtl);
      textPainter.layout(maxWidth: w - 4);
      textPainter.paint(canvas, Offset(x + (w - textPainter.width) / 2, y + (h - textPainter.height) / 2));
    }

    // Amenities
    for (final am in amenities) {
      final x = (am['posX'] as num).toDouble();
      final y = (am['posY'] as num).toDouble();
      final paint = Paint()..color = AppTheme.textSec.withOpacity(0.5);
      canvas.drawCircle(Offset(x, y), 8, paint);
    }
  }

  Color _typeColor(String type) => switch (type) {
    'Restaurant' => const Color(0xFFF59E0B),
    'Service'    => AppTheme.secondary,
    _            => AppTheme.primary,
  };

  @override
  bool shouldRepaint(_MapPainter old) =>
    old.selectedId != selectedId || old.stores.length != stores.length;
}

class _StoreDetailPanel extends StatelessWidget {
  final Map store;
  final VoidCallback onClose;
  const _StoreDetailPanel({required this.store, required this.onClose});

  @override
  Widget build(BuildContext context) => Container(
    padding: const EdgeInsets.all(16),
    decoration: const BoxDecoration(
      color: AppTheme.surface,
      border: Border(top: BorderSide(color: AppTheme.border))),
    child: Row(children: [
      Container(width: 44, height: 44,
        decoration: BoxDecoration(color: AppTheme.primary.withOpacity(0.1),
          borderRadius: BorderRadius.circular(12)),
        child: const Icon(Icons.storefront_outlined, color: AppTheme.primary)),
      const SizedBox(width: 12),
      Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Text(store['storeName'] ?? '', style: const TextStyle(
          color: AppTheme.textPri, fontWeight: FontWeight.w700)),
        Row(children: [
          const Icon(Icons.star, color: Color(0xFFF59E0B), size: 13),
          Text(' ${(store['avgRating'] ?? 0).toStringAsFixed(1)}',
            style: const TextStyle(color: AppTheme.textSec, fontSize: 12)),
          const Text(' • ', style: TextStyle(color: AppTheme.textSec)),
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 1),
            decoration: BoxDecoration(
              color: (store['isOpen'] == true ? AppTheme.secondary : AppTheme.error).withOpacity(0.15),
              borderRadius: BorderRadius.circular(6)),
            child: Text(store['isOpen'] == true ? 'مفتوح' : 'مغلق',
              style: TextStyle(
                color: store['isOpen'] == true ? AppTheme.secondary : AppTheme.error,
                fontSize: 10, fontWeight: FontWeight.w700))),
        ]),
      ])),
      Column(children: [
        ElevatedButton(
          onPressed: () {},
          style: ElevatedButton.styleFrom(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
            minimumSize: Size.zero),
          child: const Text('زيارة', style: TextStyle(fontSize: 12))),
        TextButton(onPressed: onClose,
          child: const Text('إغلاق', style: TextStyle(color: AppTheme.textSec, fontSize: 11))),
      ]),
    ]));
}
