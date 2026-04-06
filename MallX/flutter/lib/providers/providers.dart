import 'package:flutter/material.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import '../data/services/api_service.dart';

// ══════════════════════════════════════════════════════════════════════════
//  AUTH PROVIDER
// ══════════════════════════════════════════════════════════════════════════
class AuthProvider with ChangeNotifier {
  final _api     = ApiService();
  final _storage = const FlutterSecureStorage();

  CustomerProfile? _customer;
  bool _loading = false;

  CustomerProfile? get customer => _customer;
  bool get isAuthenticated => _customer != null;
  bool get isLoading => _loading;

  AuthProvider() { _restore(); }

  Future<void> _restore() async {
    final token = await _storage.read(key: 'access_token');
    if (token == null) return;
    try {
      final res = await _api.get(ApiConst.me);
      _customer = CustomerProfile.fromJson(res.data['data']);
      notifyListeners();
    } catch (_) {
      await _storage.deleteAll();
    }
  }

  Future<String?> login(String email, String password) async {
    _loading = true; notifyListeners();
    try {
      final res = await _api.post(ApiConst.login, data: {
        'email': email, 'password': password, 'mallSlug': ApiConst.mallSlug,
      });
      final data = res.data['data'];
      await _storage.write(key: 'access_token',  value: data['accessToken']);
      await _storage.write(key: 'refresh_token', value: data['refreshToken']);
      _customer = CustomerProfile.fromJson(data['customer']);
      return null;
    } catch (e) {
      return _extractError(e);
    } finally {
      _loading = false; notifyListeners();
    }
  }

  Future<String?> register({
    required String firstName, required String lastName,
    required String email, required String phone, required String password,
  }) async {
    _loading = true; notifyListeners();
    try {
      final res = await _api.post(ApiConst.register, data: {
        'firstName': firstName, 'lastName': lastName,
        'email': email, 'phone': phone,
        'password': password, 'mallSlug': ApiConst.mallSlug,
      });
      final data = res.data['data'];
      await _storage.write(key: 'access_token',  value: data['accessToken']);
      await _storage.write(key: 'refresh_token', value: data['refreshToken']);
      _customer = CustomerProfile.fromJson(data['customer']);
      return null;
    } catch (e) {
      return _extractError(e);
    } finally {
      _loading = false; notifyListeners();
    }
  }

  Future<void> logout() async {
    try {
      final rt = await _storage.read(key: 'refresh_token');
      await _api.post(ApiConst.logout, data: {'refreshToken': rt ?? ''});
    } catch (_) {}
    await _storage.deleteAll();
    _customer = null;
    notifyListeners();
  }

  String _extractError(dynamic e) {
    if (e is Exception) {
      try {
        final data = (e as dynamic).response?.data;
        return data?['error'] ?? 'حدث خطأ غير متوقع';
      } catch (_) {}
    }
    return 'تعذر الاتصال بالخادم';
  }
}

// ══════════════════════════════════════════════════════════════════════════
//  CART PROVIDER
// ══════════════════════════════════════════════════════════════════════════
class CartProvider with ChangeNotifier {
  final _api = ApiService();

  CartDto _cart = CartDto.empty();
  bool _loading = false;

  CartDto get cart => _cart;
  bool get isLoading => _loading;
  int  get itemCount => _cart.itemCount;
  bool get isEmpty   => _cart.itemCount == 0;

  Future<void> load() async {
    try {
      final res = await _api.get(ApiConst.cart);
      _cart = CartDto.fromJson(res.data['data']);
      notifyListeners();
    } catch (_) {}
  }

  Future<String?> addItem({
    required String productId, required String storeId,
    int quantity = 1, String? notes,
  }) async {
    _loading = true; notifyListeners();
    try {
      final res = await _api.post(ApiConst.cartItems, data: {
        'productId': productId, 'storeId': storeId,
        'quantity': quantity, 'notes': notes,
      });
      _cart = CartDto.fromJson(res.data['data']);
      return null;
    } catch (e) {
      return _extractError(e);
    } finally {
      _loading = false; notifyListeners();
    }
  }

  Future<void> updateItem(String productId, int qty) async {
    try {
      final res = await _api.put(ApiConst.cartItems,
          data: {'productId': productId, 'quantity': qty});
      _cart = CartDto.fromJson(res.data['data']);
      notifyListeners();
    } catch (_) {}
  }

  Future<void> removeItem(String productId) async {
    try {
      final res = await _api.delete('${ApiConst.cartItems}/$productId');
      _cart = CartDto.fromJson(res.data['data']);
      notifyListeners();
    } catch (_) {}
  }

  Future<String?> checkout({
    required String fulfillmentType,
    String paymentMethod = 'Cash',
    String? deliveryAddress,
    String? notes,
  }) async {
    _loading = true; notifyListeners();
    try {
      final res = await _api.post(ApiConst.checkout, data: {
        'fulfillmentType': fulfillmentType,
        'paymentMethod':   paymentMethod,
        'deliveryAddress': deliveryAddress,
        'notes':           notes,
      });
      _cart = CartDto.empty();
      notifyListeners();
      return null;
    } catch (e) {
      return _extractError(e);
    } finally {
      _loading = false; notifyListeners();
    }
  }

  String _extractError(dynamic e) {
    try {
      return (e as dynamic).response?.data?['error'] ?? 'حدث خطأ';
    } catch (_) { return 'تعذر الاتصال بالخادم'; }
  }
}

// ══════════════════════════════════════════════════════════════════════════
//  MALL PROVIDER (Orders)
// ══════════════════════════════════════════════════════════════════════════
class MallProvider with ChangeNotifier {
  final _api = ApiService();

  List<MallOrderDto> _orders = [];
  MallOrderDto? _selectedOrder;
  bool _loading = false;

  List<MallOrderDto> get orders => _orders;
  MallOrderDto? get selectedOrder => _selectedOrder;
  bool get isLoading => _loading;

  Future<void> loadOrders() async {
    _loading = true; notifyListeners();
    try {
      final res = await _api.get(ApiConst.orders);
      final list = res.data['data'] as List? ?? [];
      _orders = list.map((o) => MallOrderDto.fromJson(o)).toList();
    } catch (_) {}
    _loading = false; notifyListeners();
  }

  Future<void> loadOrder(String orderId) async {
    try {
      final res = await _api.get('${ApiConst.orders}/$orderId');
      _selectedOrder = MallOrderDto.fromJson(res.data['data']);
      notifyListeners();
    } catch (_) {}
  }
}
