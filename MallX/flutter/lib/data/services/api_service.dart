import 'package:dio/dio.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

// ─── API Constants ────────────────────────────────────────────────────────
class ApiConst {
  static const String baseUrl  = 'http://YOUR_SERVER_IP:5000/api';
  static const String mallSlug = 'mallx-demo'; // يُعدَّل حسب المول

  // Auth
  static const String register  = '/mall/auth/register';
  static const String login     = '/mall/auth/login';
  static const String refresh   = '/mall/auth/refresh';
  static const String logout    = '/mall/auth/logout';
  static const String me        = '/mall/auth/me';

  // Cart
  static const String cart      = '/mall/cart';
  static const String cartItems = '/mall/cart/items';

  // Orders
  static const String checkout  = '/mall/orders/checkout';
  static const String orders    = '/mall/orders';
}

// ─── API Service ──────────────────────────────────────────────────────────
class ApiService {
  static final ApiService _instance = ApiService._();
  factory ApiService() => _instance;
  ApiService._() { _init(); }

  late Dio _dio;
  final _storage = const FlutterSecureStorage();

  void _init() {
    _dio = Dio(BaseOptions(
      baseUrl: ApiConst.baseUrl,
      connectTimeout: const Duration(seconds: 15),
      receiveTimeout: const Duration(seconds: 15),
      headers: {'Content-Type': 'application/json'},
    ));

    _dio.interceptors.add(InterceptorsWrapper(
      onRequest: (opts, handler) async {
        final token = await _storage.read(key: 'access_token');
        if (token != null) opts.headers['Authorization'] = 'Bearer $token';
        handler.next(opts);
      },
      onError: (err, handler) async {
        if (err.response?.statusCode == 401) {
          final refreshed = await _tryRefresh();
          if (refreshed) {
            final token = await _storage.read(key: 'access_token');
            err.requestOptions.headers['Authorization'] = 'Bearer $token';
            final retry = await _dio.fetch(err.requestOptions);
            handler.resolve(retry);
            return;
          }
        }
        handler.next(err);
      },
    ));
  }

  Future<bool> _tryRefresh() async {
    try {
      final rt = await _storage.read(key: 'refresh_token');
      if (rt == null) return false;
      final res = await Dio().post('${ApiConst.baseUrl}${ApiConst.refresh}',
          data: {'refreshToken': rt});
      final data = res.data['data'];
      await _storage.write(key: 'access_token',  value: data['accessToken']);
      await _storage.write(key: 'refresh_token', value: data['refreshToken']);
      return true;
    } catch (_) { return false; }
  }

  Future<Response> get(String path, {Map<String, dynamic>? params}) =>
      _dio.get(path, queryParameters: params);

  Future<Response> post(String path, {dynamic data}) =>
      _dio.post(path, data: data);

  Future<Response> put(String path, {dynamic data}) =>
      _dio.put(path, data: data);

  Future<Response> patch(String path, {dynamic data}) =>
      _dio.patch(path, data: data);

  Future<Response> delete(String path) => _dio.delete(path);
}

// ══════════════════════════════════════════════════════════════════════════
//  MODELS
// ══════════════════════════════════════════════════════════════════════════

class CustomerProfile {
  final String id, firstName, lastName, email, tier;
  final String? phone, avatarUrl;
  final int loyaltyPoints, pointsToNext;

  CustomerProfile({
    required this.id, required this.firstName, required this.lastName,
    required this.email, required this.tier, this.phone, this.avatarUrl,
    required this.loyaltyPoints, required this.pointsToNext,
  });

  String get fullName => '$firstName $lastName';

  factory CustomerProfile.fromJson(Map<String, dynamic> j) => CustomerProfile(
    id: j['id'], firstName: j['firstName'], lastName: j['lastName'],
    email: j['email'], tier: j['tier'], phone: j['phone'],
    avatarUrl: j['avatarUrl'],
    loyaltyPoints: j['loyaltyPoints'] ?? 0,
    pointsToNext: j['pointsToNext'] ?? 0,
  );
}

class CartDto {
  final String id;
  final List<CartStoreGroup> stores;
  final double subtotal, deliveryFee, total;
  final int itemCount;

  CartDto({required this.id, required this.stores, required this.subtotal,
    required this.deliveryFee, required this.total, required this.itemCount});

  factory CartDto.fromJson(Map<String, dynamic> j) => CartDto(
    id: j['id'] ?? '',
    stores: (j['stores'] as List? ?? [])
        .map((s) => CartStoreGroup.fromJson(s)).toList(),
    subtotal: (j['subtotal'] as num?)?.toDouble() ?? 0,
    deliveryFee: (j['deliveryFee'] as num?)?.toDouble() ?? 0,
    total: (j['total'] as num?)?.toDouble() ?? 0,
    itemCount: j['itemCount'] ?? 0,
  );

  static CartDto empty() => CartDto(
    id: '', stores: [], subtotal: 0, deliveryFee: 0, total: 0, itemCount: 0);
}

class CartStoreGroup {
  final String storeId, storeName, storeType;
  final List<CartItemDto> items;
  final double storeSubtotal;

  CartStoreGroup({required this.storeId, required this.storeName,
    required this.storeType, required this.items, required this.storeSubtotal});

  factory CartStoreGroup.fromJson(Map<String, dynamic> j) => CartStoreGroup(
    storeId: j['storeId'], storeName: j['storeName'], storeType: j['storeType'],
    storeSubtotal: (j['storeSubtotal'] as num?)?.toDouble() ?? 0,
    items: (j['items'] as List? ?? []).map((i) => CartItemDto.fromJson(i)).toList(),
  );
}

class CartItemDto {
  final String cartItemId, productId, productName;
  final String? imageUrl, notes;
  final double unitPrice, lineTotal;
  final int quantity;
  final bool inStock;

  CartItemDto({required this.cartItemId, required this.productId,
    required this.productName, this.imageUrl, this.notes,
    required this.unitPrice, required this.lineTotal,
    required this.quantity, required this.inStock});

  factory CartItemDto.fromJson(Map<String, dynamic> j) => CartItemDto(
    cartItemId: j['cartItemId'], productId: j['productId'],
    productName: j['productName'], imageUrl: j['imageUrl'], notes: j['notes'],
    unitPrice: (j['unitPrice'] as num).toDouble(),
    lineTotal: (j['lineTotal'] as num).toDouble(),
    quantity: j['quantity'], inStock: j['inStock'] ?? true,
  );
}

class MallOrderDto {
  final String id, orderNumber, status, fulfillmentType, paymentMethod;
  final double subtotal, deliveryFee, total;
  final DateTime placedAt;
  final List<StoreOrderDto> storeOrders;
  final List<OrderStatusHistoryDto> timeline;

  MallOrderDto({required this.id, required this.orderNumber, required this.status,
    required this.fulfillmentType, required this.paymentMethod,
    required this.subtotal, required this.deliveryFee, required this.total,
    required this.placedAt, required this.storeOrders, required this.timeline});

  factory MallOrderDto.fromJson(Map<String, dynamic> j) => MallOrderDto(
    id: j['id'], orderNumber: j['orderNumber'], status: j['status'],
    fulfillmentType: j['fulfillmentType'], paymentMethod: j['paymentMethod'],
    subtotal: (j['subtotal'] as num).toDouble(),
    deliveryFee: (j['deliveryFee'] as num).toDouble(),
    total: (j['total'] as num).toDouble(),
    placedAt: DateTime.parse(j['placedAt']),
    storeOrders: (j['storeOrders'] as List? ?? [])
        .map((s) => StoreOrderDto.fromJson(s)).toList(),
    timeline: (j['timeline'] as List? ?? [])
        .map((t) => OrderStatusHistoryDto.fromJson(t)).toList(),
  );
}

class StoreOrderDto {
  final String id, storeId, storeName, status;
  final double subtotal, storeTotal;
  final List<StoreOrderItemDto> items;

  StoreOrderDto({required this.id, required this.storeId, required this.storeName,
    required this.status, required this.subtotal, required this.storeTotal,
    required this.items});

  factory StoreOrderDto.fromJson(Map<String, dynamic> j) => StoreOrderDto(
    id: j['id'], storeId: j['storeId'], storeName: j['storeName'],
    status: j['status'],
    subtotal: (j['subtotal'] as num).toDouble(),
    storeTotal: (j['storeTotal'] as num).toDouble(),
    items: (j['items'] as List? ?? []).map((i) => StoreOrderItemDto.fromJson(i)).toList(),
  );
}

class StoreOrderItemDto {
  final String productName;
  final int quantity;
  final double unitPrice, total;
  final String? notes;

  StoreOrderItemDto({required this.productName, required this.quantity,
    required this.unitPrice, required this.total, this.notes});

  factory StoreOrderItemDto.fromJson(Map<String, dynamic> j) => StoreOrderItemDto(
    productName: j['productName'], quantity: j['quantity'],
    unitPrice: (j['unitPrice'] as num).toDouble(),
    total: (j['total'] as num).toDouble(), notes: j['notes'],
  );
}

class OrderStatusHistoryDto {
  final String newStatus;
  final String? note;
  final DateTime createdAt;

  OrderStatusHistoryDto({required this.newStatus, this.note, required this.createdAt});

  factory OrderStatusHistoryDto.fromJson(Map<String, dynamic> j) =>
    OrderStatusHistoryDto(
      newStatus: j['newStatus'], note: j['note'],
      createdAt: DateTime.parse(j['createdAt']),
    );
}
