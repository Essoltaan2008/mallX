import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../../core/theme/app_theme.dart';
import '../../providers/providers.dart';

// ══════════════════════════════════════════════════════════════════════════
//  MAIN NAV SCREEN (Bottom Navigation)
// ══════════════════════════════════════════════════════════════════════════
class MainNavScreen extends StatefulWidget {
  const MainNavScreen({super.key});
  @override State<MainNavScreen> createState() => _MainNavScreenState();
}

class _MainNavScreenState extends State<MainNavScreen> {
  int _index = 0;

  final _pages = const [
    HomeScreen(), CartScreen(), OrdersScreen(), ProfileScreen(),
  ];

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      context.read<CartProvider>().load();
    });
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: IndexedStack(index: _index, children: _pages),
      bottomNavigationBar: Consumer<CartProvider>(
        builder: (_, cart, __) => BottomNavigationBar(
          currentIndex: _index,
          onTap: (i) => setState(() => _index = i),
          items: [
            const BottomNavigationBarItem(
                icon: Icon(Icons.home_outlined),
                activeIcon: Icon(Icons.home),
                label: 'الرئيسية'),
            BottomNavigationBarItem(
                icon: Badge(
                  isLabelVisible: cart.itemCount > 0,
                  label: Text('${cart.itemCount}'),
                  child: const Icon(Icons.shopping_cart_outlined),
                ),
                activeIcon: const Icon(Icons.shopping_cart),
                label: 'السلة'),
            const BottomNavigationBarItem(
                icon: Icon(Icons.receipt_long_outlined),
                activeIcon: Icon(Icons.receipt_long),
                label: 'طلباتي'),
            const BottomNavigationBarItem(
                icon: Icon(Icons.person_outline),
                activeIcon: Icon(Icons.person),
                label: 'حسابي'),
          ],
        ),
      ),
    );
  }
}

// ══════════════════════════════════════════════════════════════════════════
//  HOME SCREEN
// ══════════════════════════════════════════════════════════════════════════
class HomeScreen extends StatelessWidget {
  const HomeScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final customer = context.watch<AuthProvider>().customer;
    return Scaffold(
      body: CustomScrollView(
        slivers: [
          // Header
          SliverAppBar(
            expandedHeight: 160,
            pinned: true,
            flexibleSpace: FlexibleSpaceBar(
              background: Container(
                decoration: const BoxDecoration(
                  gradient: LinearGradient(
                    colors: [Color(0xFF1E3A5F), Color(0xFF0A0F1A)],
                    begin: Alignment.topLeft, end: Alignment.bottomRight,
                  ),
                ),
                padding: const EdgeInsets.fromLTRB(24, 60, 24, 20),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      'مرحباً، ${customer?.firstName ?? ''} 👋',
                      style: const TextStyle(color: Colors.white70, fontSize: 14),
                    ),
                    const SizedBox(height: 4),
                    const Text('اكتشف أفضل العروض',
                      style: TextStyle(color: Colors.white, fontSize: 22,
                          fontWeight: FontWeight.w800)),
                    const SizedBox(height: 16),
                    // Loyalty badge
                    if (customer != null)
                      Container(
                        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
                        decoration: BoxDecoration(
                          color: Colors.white12,
                          borderRadius: BorderRadius.circular(20),
                        ),
                        child: Row(mainAxisSize: MainAxisSize.min, children: [
                          const Icon(Icons.stars, color: Color(0xFFF59E0B), size: 16),
                          const SizedBox(width: 6),
                          Text(
                            '${customer.loyaltyPoints} نقطة — ${customer.tier}',
                            style: const TextStyle(color: Colors.white, fontSize: 12),
                          ),
                        ]),
                      ),
                  ],
                ),
              ),
            ),
          ),

          // Content
          SliverPadding(
            padding: const EdgeInsets.all(16),
            sliver: SliverList(
              delegate: SliverChildListDelegate([
                // Quick Actions
                const Text('تصفح حسب النوع',
                  style: TextStyle(fontWeight: FontWeight.w700,
                      fontSize: 16, color: AppTheme.textPri)),
                const SizedBox(height: 12),
                Row(children: [
                  _TypeCard(icon: Icons.restaurant_menu, label: 'مطاعم',
                      color: const Color(0xFFF59E0B)),
                  const SizedBox(width: 10),
                  _TypeCard(icon: Icons.shopping_bag_outlined, label: 'متاجر',
                      color: AppTheme.primary),
                  const SizedBox(width: 10),
                  _TypeCard(icon: Icons.spa_outlined, label: 'خدمات',
                      color: AppTheme.secondary),
                ].map((w) => Expanded(child: w)).toList()),
                const SizedBox(height: 24),

                // Placeholder stores
                const Text('المحلات المتاحة',
                  style: TextStyle(fontWeight: FontWeight.w700,
                      fontSize: 16, color: AppTheme.textPri)),
                const SizedBox(height: 12),
                ...List.generate(3, (i) => _StoreCard(index: i)),
              ]),
            ),
          ),
        ],
      ),
    );
  }
}

class _TypeCard extends StatelessWidget {
  final IconData icon;
  final String label;
  final Color color;
  const _TypeCard({required this.icon, required this.label, required this.color});

  @override
  Widget build(BuildContext context) => Container(
    padding: const EdgeInsets.symmetric(vertical: 16),
    decoration: BoxDecoration(
      color: color.withOpacity(0.1),
      borderRadius: BorderRadius.circular(14),
      border: Border.all(color: color.withOpacity(0.3)),
    ),
    child: Column(children: [
      Icon(icon, color: color, size: 28),
      const SizedBox(height: 8),
      Text(label, style: TextStyle(color: color, fontWeight: FontWeight.w600, fontSize: 13)),
    ]),
  );
}

class _StoreCard extends StatelessWidget {
  final int index;
  const _StoreCard({required this.index});
  static const _names = ['مطعم الشيف', 'متجر الموضة', 'صالون بيوتي'];
  static const _types = ['مطعم', 'متجر', 'صالون'];

  @override
  Widget build(BuildContext context) => Container(
    margin: const EdgeInsets.only(bottom: 12),
    padding: const EdgeInsets.all(16),
    decoration: BoxDecoration(
      color: AppTheme.card,
      borderRadius: BorderRadius.circular(16),
      border: Border.all(color: AppTheme.border),
    ),
    child: Row(children: [
      Container(
        width: 56, height: 56,
        decoration: BoxDecoration(
          color: AppTheme.primary.withOpacity(0.1),
          borderRadius: BorderRadius.circular(14),
        ),
        child: const Icon(Icons.storefront_outlined, color: AppTheme.primary),
      ),
      const SizedBox(width: 14),
      Expanded(child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(_names[index], style: const TextStyle(
              fontWeight: FontWeight.w700, color: AppTheme.textPri, fontSize: 15)),
          const SizedBox(height: 4),
          Text(_types[index], style: const TextStyle(
              color: AppTheme.textSec, fontSize: 12)),
        ],
      )),
      const Icon(Icons.chevron_left, color: AppTheme.textSec),
    ]),
  );
}

// ══════════════════════════════════════════════════════════════════════════
//  CART SCREEN
// ══════════════════════════════════════════════════════════════════════════
class CartScreen extends StatelessWidget {
  const CartScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('سلة التسوق')),
      body: Consumer<CartProvider>(
        builder: (_, cart, __) {
          if (cart.isLoading) return const Center(child: CircularProgressIndicator());
          if (cart.isEmpty) return const _EmptyCart();
          return Column(children: [
            Expanded(
              child: ListView(
                padding: const EdgeInsets.all(16),
                children: cart.cart.stores.map((store) =>
                    _StoreSection(group: store)).toList(),
              ),
            ),
            _CartSummary(cart: cart),
          ]);
        },
      ),
    );
  }
}

class _EmptyCart extends StatelessWidget {
  const _EmptyCart();
  @override
  Widget build(BuildContext context) => Center(
    child: Column(mainAxisAlignment: MainAxisAlignment.center, children: [
      Icon(Icons.shopping_cart_outlined, size: 80, color: AppTheme.textSec.withOpacity(0.3)),
      const SizedBox(height: 16),
      const Text('سلتك فارغة', style: TextStyle(color: AppTheme.textSec, fontSize: 16)),
      const SizedBox(height: 8),
      const Text('ابدأ التسوق من الشاشة الرئيسية',
          style: TextStyle(color: AppTheme.textSec, fontSize: 13)),
    ]),
  );
}

class _StoreSection extends StatelessWidget {
  final CartStoreGroup group;
  const _StoreSection({required this.group});

  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      Padding(
        padding: const EdgeInsets.symmetric(vertical: 8),
        child: Row(children: [
          const Icon(Icons.storefront_outlined, size: 16, color: AppTheme.textSec),
          const SizedBox(width: 8),
          Text(group.storeName, style: const TextStyle(
              color: AppTheme.textSec, fontWeight: FontWeight.w600, fontSize: 13)),
        ]),
      ),
      ...group.items.map((item) => _CartItemTile(item: item)),
      const Divider(color: AppTheme.border, height: 24),
    ],
  );
}

class _CartItemTile extends StatelessWidget {
  final CartItemDto item;
  const _CartItemTile({required this.item});

  @override
  Widget build(BuildContext context) {
    final cart = context.read<CartProvider>();
    return Container(
      margin: const EdgeInsets.only(bottom: 8),
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: AppTheme.card, borderRadius: BorderRadius.circular(12),
        border: Border.all(color: AppTheme.border),
      ),
      child: Row(children: [
        Expanded(child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(item.productName, style: const TextStyle(
                color: AppTheme.textPri, fontWeight: FontWeight.w600)),
            const SizedBox(height: 4),
            Text('${item.unitPrice.toStringAsFixed(0)} ج.م',
                style: const TextStyle(color: AppTheme.primary, fontSize: 13)),
          ],
        )),
        Row(children: [
          _QtyBtn(Icons.remove, () => cart.updateItem(item.productId, item.quantity - 1)),
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 12),
            child: Text('${item.quantity}',
                style: const TextStyle(color: AppTheme.textPri, fontWeight: FontWeight.w700)),
          ),
          _QtyBtn(Icons.add, () => cart.updateItem(item.productId, item.quantity + 1)),
        ]),
      ]),
    );
  }
}

class _QtyBtn extends StatelessWidget {
  final IconData icon;
  final VoidCallback onTap;
  const _QtyBtn(this.icon, this.onTap);
  @override
  Widget build(BuildContext context) => GestureDetector(
    onTap: onTap,
    child: Container(
      width: 28, height: 28,
      decoration: BoxDecoration(
          color: AppTheme.border, borderRadius: BorderRadius.circular(8)),
      child: Icon(icon, size: 16, color: AppTheme.textPri),
    ),
  );
}

class _CartSummary extends StatelessWidget {
  final CartProvider cart;
  const _CartSummary({required this.cart});

  Future<void> _checkout(BuildContext context) async {
    final error = await cart.checkout(fulfillmentType: 'Delivery');
    if (!context.mounted) return;
    if (error != null) {
      ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(error), backgroundColor: AppTheme.error));
    } else {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('✅ تم تأكيد طلبك بنجاح!'),
            backgroundColor: AppTheme.secondary));
    }
  }

  @override
  Widget build(BuildContext context) => Container(
    padding: const EdgeInsets.all(20),
    decoration: const BoxDecoration(
      color: AppTheme.surface,
      border: Border(top: BorderSide(color: AppTheme.border)),
    ),
    child: Column(mainAxisSize: MainAxisSize.min, children: [
      Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
        const Text('المجموع الفرعي', style: TextStyle(color: AppTheme.textSec)),
        Text('${cart.cart.subtotal.toStringAsFixed(0)} ج.م',
            style: const TextStyle(color: AppTheme.textPri, fontWeight: FontWeight.w600)),
      ]),
      const SizedBox(height: 6),
      Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
        const Text('رسوم التوصيل', style: TextStyle(color: AppTheme.textSec)),
        Text('${cart.cart.deliveryFee.toStringAsFixed(0)} ج.م',
            style: const TextStyle(color: AppTheme.textPri)),
      ]),
      const Divider(color: AppTheme.border, height: 20),
      Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
        const Text('الإجمالي', style: TextStyle(color: AppTheme.textPri,
            fontWeight: FontWeight.w800, fontSize: 16)),
        Text('${cart.cart.total.toStringAsFixed(0)} ج.م',
            style: const TextStyle(color: AppTheme.primary,
                fontWeight: FontWeight.w800, fontSize: 18)),
      ]),
      const SizedBox(height: 16),
      ElevatedButton(
        onPressed: cart.isLoading ? null : () => _checkout(context),
        child: cart.isLoading
            ? const SizedBox(width: 20, height: 20,
                child: CircularProgressIndicator(color: Colors.white, strokeWidth: 2))
            : const Text('تأكيد الطلب'),
      ),
    ]),
  );
}

// ══════════════════════════════════════════════════════════════════════════
//  ORDERS SCREEN
// ══════════════════════════════════════════════════════════════════════════
class OrdersScreen extends StatefulWidget {
  const OrdersScreen({super.key});
  @override State<OrdersScreen> createState() => _OrdersScreenState();
}

class _OrdersScreenState extends State<OrdersScreen> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      context.read<MallProvider>().loadOrders();
    });
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('طلباتي')),
      body: Consumer<MallProvider>(
        builder: (_, mall, __) {
          if (mall.isLoading) return const Center(child: CircularProgressIndicator());
          if (mall.orders.isEmpty) return const Center(
            child: Text('لا يوجد طلبات بعد',
                style: TextStyle(color: AppTheme.textSec)));
          return ListView.separated(
            padding: const EdgeInsets.all(16),
            itemCount: mall.orders.length,
            separatorBuilder: (_, __) => const SizedBox(height: 10),
            itemBuilder: (_, i) => _OrderCard(order: mall.orders[i]),
          );
        },
      ),
    );
  }
}

class _OrderCard extends StatelessWidget {
  final MallOrderDto order;
  const _OrderCard({required this.order});

  Color _statusColor(String s) => switch (s) {
    'Delivered' => AppTheme.secondary,
    'Placed' || 'Confirmed' => AppTheme.accent,
    'Preparing' || 'Ready' => AppTheme.primary,
    'Cancelled' => AppTheme.error,
    _ => AppTheme.textSec,
  };

  String _statusAr(String s) => switch (s) {
    'Placed'    => 'تم الاستلام',
    'Confirmed' => 'مؤكد',
    'Preparing' => 'قيد التحضير',
    'Ready'     => 'جاهز',
    'PickedUp'  => 'في الطريق',
    'Delivered' => 'تم التسليم',
    'Cancelled' => 'ملغى',
    _ => s,
  };

  @override
  Widget build(BuildContext context) => Container(
    padding: const EdgeInsets.all(16),
    decoration: BoxDecoration(
      color: AppTheme.card, borderRadius: BorderRadius.circular(16),
      border: Border.all(color: AppTheme.border),
    ),
    child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
      Row(mainAxisAlignment: MainAxisAlignment.spaceBetween, children: [
        Text(order.orderNumber, style: const TextStyle(
            color: AppTheme.textPri, fontWeight: FontWeight.w700, fontSize: 15)),
        Container(
          padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
          decoration: BoxDecoration(
            color: _statusColor(order.status).withOpacity(0.15),
            borderRadius: BorderRadius.circular(10),
          ),
          child: Text(_statusAr(order.status),
              style: TextStyle(color: _statusColor(order.status),
                  fontSize: 12, fontWeight: FontWeight.w600)),
        ),
      ]),
      const SizedBox(height: 12),
      Text('${order.storeOrders.length} محل — ${order.total.toStringAsFixed(0)} ج.م',
          style: const TextStyle(color: AppTheme.textSec, fontSize: 13)),
      const SizedBox(height: 4),
      Text(
        '${order.placedAt.day}/${order.placedAt.month}/${order.placedAt.year}',
        style: const TextStyle(color: AppTheme.textSec, fontSize: 12),
      ),
    ]),
  );
}

// ══════════════════════════════════════════════════════════════════════════
//  PROFILE SCREEN
// ══════════════════════════════════════════════════════════════════════════
class ProfileScreen extends StatelessWidget {
  const ProfileScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final customer = context.watch<AuthProvider>().customer;
    return Scaffold(
      appBar: AppBar(title: const Text('حسابي')),
      body: customer == null
          ? const SizedBox()
          : ListView(padding: const EdgeInsets.all(16), children: [
              // Avatar + name
              Center(child: Column(children: [
                CircleAvatar(radius: 40,
                  backgroundColor: AppTheme.primary.withOpacity(0.2),
                  child: Text(customer.firstName.substring(0, 1),
                    style: const TextStyle(fontSize: 32, color: AppTheme.primary,
                        fontWeight: FontWeight.w800))),
                const SizedBox(height: 12),
                Text(customer.fullName, style: const TextStyle(
                    color: AppTheme.textPri, fontSize: 20, fontWeight: FontWeight.w700)),
                const SizedBox(height: 4),
                Text(customer.email, style: const TextStyle(
                    color: AppTheme.textSec, fontSize: 13)),
              ])),
              const SizedBox(height: 24),

              // Loyalty card
              Container(
                padding: const EdgeInsets.all(20),
                decoration: BoxDecoration(
                  gradient: const LinearGradient(
                    colors: [Color(0xFF1E3A5F), Color(0xFF2D1B6B)],
                    begin: Alignment.topLeft, end: Alignment.bottomRight,
                  ),
                  borderRadius: BorderRadius.circular(20),
                ),
                child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  Row(children: [
                    const Icon(Icons.stars, color: Color(0xFFF59E0B), size: 20),
                    const SizedBox(width: 8),
                    Text(customer.tier, style: const TextStyle(
                        color: Color(0xFFF59E0B), fontWeight: FontWeight.w700)),
                  ]),
                  const SizedBox(height: 12),
                  Text('${customer.loyaltyPoints}',
                    style: const TextStyle(color: Colors.white, fontSize: 36,
                        fontWeight: FontWeight.w800)),
                  const Text('نقطة مكتسبة', style: TextStyle(color: Colors.white70)),
                  if (customer.pointsToNext > 0) ...[
                    const SizedBox(height: 12),
                    Text('${customer.pointsToNext} نقطة للمستوى التالي',
                      style: const TextStyle(color: Colors.white60, fontSize: 12)),
                    const SizedBox(height: 6),
                    ClipRRect(
                      borderRadius: BorderRadius.circular(4),
                      child: LinearProgressIndicator(
                        value: 1 - (customer.pointsToNext /
                            (customer.tier == 'Bronze' ? 1000 : 5000)),
                        backgroundColor: Colors.white12,
                        valueColor: const AlwaysStoppedAnimation(Color(0xFFF59E0B)),
                        minHeight: 6,
                      ),
                    ),
                  ],
                ]),
              ),
              const SizedBox(height: 24),

              // Menu
              ...[
                ('طلباتي', Icons.receipt_long_outlined),
                ('عناويني', Icons.location_on_outlined),
                ('الإعدادات', Icons.settings_outlined),
              ].map((item) => ListTile(
                leading: Icon(item.$2, color: AppTheme.textSec),
                title: Text(item.$1, style: const TextStyle(color: AppTheme.textPri)),
                trailing: const Icon(Icons.chevron_left, color: AppTheme.textSec),
                onTap: () {},
              )),
              const Divider(color: AppTheme.border),
              ListTile(
                leading: const Icon(Icons.logout, color: AppTheme.error),
                title: const Text('تسجيل الخروج',
                    style: TextStyle(color: AppTheme.error)),
                onTap: () => context.read<AuthProvider>().logout(),
              ),
            ]),
    );
  }
}
