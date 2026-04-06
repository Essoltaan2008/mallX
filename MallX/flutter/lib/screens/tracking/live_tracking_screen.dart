import 'dart:async';
import 'dart:math';
import 'package:flutter/material.dart';
import '../../core/theme/app_theme.dart';
import '../../data/services/api_service.dart';

// ══════════════════════════════════════════════════════════════════════════
//  SIGNALR CONNECTION (simplified — uses http polling fallback)
//  In production: use signalr_netcore package
// ══════════════════════════════════════════════════════════════════════════
class SignalRConnection {
  final String orderId;
  final Function(Map) onOrderStatus;
  final Function(Map) onDriverLocation;

  Timer? _pollTimer;
  final _api = ApiService();

  SignalRConnection({
    required this.orderId,
    required this.onOrderStatus,
    required this.onDriverLocation,
  });

  void start() {
    // Poll order status every 15s as fallback
    _pollTimer = Timer.periodic(const Duration(seconds: 15), (_) => _poll());
    _poll();
  }

  Future<void> _poll() async {
    try {
      final res = await _api.get('/mall/orders/$orderId');
      final data = res.data['data'] as Map?;
      if (data != null) onOrderStatus(data);
    } catch (_) {}
  }

  void stop() => _pollTimer?.cancel();
}

// ══════════════════════════════════════════════════════════════════════════
//  LIVE ORDER TRACKING SCREEN (Real-time SignalR + Map)
// ══════════════════════════════════════════════════════════════════════════
class LiveTrackingScreen extends StatefulWidget {
  final String orderId, orderNumber;
  final String? initialStatus;

  const LiveTrackingScreen({
    super.key,
    required this.orderId,
    required this.orderNumber,
    this.initialStatus,
  });

  @override
  State<LiveTrackingScreen> createState() => _LiveTrackingScreenState();
}

class _LiveTrackingScreenState extends State<LiveTrackingScreen>
    with TickerProviderStateMixin {
  late SignalRConnection _connection;
  late AnimationController _pulseCtrl;
  late AnimationController _moveCtrl;
  late Animation<double> _pulseAnim;

  String _status     = 'Placed';
  String _statusMsg  = 'تم استلام طلبك';
  bool   _connected  = false;

  // Driver position (simulated for demo)
  double _driverLat  = 30.0444;
  double _driverLng  = 31.2357;
  double _driverHeading = 45;

  // Order steps
  static const _steps = [
    ('Placed',    'تم الاستلام',    '📋', AppTheme.textSec),
    ('Confirmed', 'مؤكد من المحل',  '✅', Color(0xFF3B82F6)),
    ('Preparing', 'قيد التحضير',    '👨‍🍳', Color(0xFF8B5CF6)),
    ('Ready',     'جاهز للتسليم',   '📦', Color(0xFFF59E0B)),
    ('PickedUp',  'مع السائق',       '🚗', AppTheme.primary),
    ('Delivered', 'تم التسليم!',     '🎉', AppTheme.secondary),
  ];

  @override
  void initState() {
    super.initState();
    _status = widget.initialStatus ?? 'Placed';

    _pulseCtrl = AnimationController(
      vsync: this, duration: const Duration(seconds: 2))..repeat(reverse: true);
    _pulseAnim = Tween(begin: 0.8, end: 1.2).animate(
      CurvedAnimation(parent: _pulseCtrl, curve: Curves.easeInOut));

    _moveCtrl = AnimationController(
      vsync: this, duration: const Duration(seconds: 3))..repeat();

    _connection = SignalRConnection(
      orderId: widget.orderId,
      onOrderStatus: _handleStatus,
      onDriverLocation: _handleDriverLocation,
    );
    _connection.start();

    // Simulate driver movement for demo
    if (_status == 'PickedUp') _startDriverSimulation();
  }

  @override
  void dispose() {
    _connection.stop();
    _pulseCtrl.dispose();
    _moveCtrl.dispose();
    super.dispose();
  }

  void _handleStatus(Map data) {
    if (!mounted) return;
    final newStatus = data['status'] as String? ?? _status;
    setState(() {
      _status    = newStatus;
      _statusMsg = data['message'] as String? ?? _getDefaultMsg(newStatus);
      _connected = true;
    });
    if (newStatus == 'PickedUp') _startDriverSimulation();
  }

  void _handleDriverLocation(Map data) {
    if (!mounted) return;
    setState(() {
      _driverLat     = (data['lat']     as num?)?.toDouble() ?? _driverLat;
      _driverLng     = (data['lng']     as num?)?.toDouble() ?? _driverLng;
      _driverHeading = (data['heading'] as num?)?.toDouble() ?? _driverHeading;
    });
  }

  Timer? _simTimer;
  void _startDriverSimulation() {
    _simTimer = Timer.periodic(const Duration(seconds: 3), (_) {
      if (!mounted) return;
      setState(() {
        _driverLat += (Random().nextDouble() - 0.5) * 0.002;
        _driverLng += (Random().nextDouble() - 0.5) * 0.002;
        _driverHeading = (Random().nextDouble() * 360);
      });
    });
  }

  String _getDefaultMsg(String status) => switch (status) {
    'Placed'    => 'تم استلام طلبك بنجاح ✅',
    'Confirmed' => 'المحل أكّد استلام طلبك',
    'Preparing' => 'جاري تحضير طلبك الآن 👨‍🍳',
    'Ready'     => 'طلبك جاهز في انتظار السائق',
    'PickedUp'  => 'السائق في طريقه إليك! 🚗',
    'Delivered' => 'تم التسليم! شاركنا رأيك 🎉',
    _           => '',
  };

  Color _statusColor(String s) => switch (s) {
    'Placed'    => AppTheme.textSec,
    'Confirmed' => const Color(0xFF3B82F6),
    'Preparing' => const Color(0xFF8B5CF6),
    'Ready'     => const Color(0xFFF59E0B),
    'PickedUp'  => AppTheme.primary,
    'Delivered' => AppTheme.secondary,
    _           => AppTheme.textSec,
  };

  int _currentStepIndex() =>
    _steps.indexWhere((s) => s.$1 == _status).clamp(0, _steps.length - 1);

  @override
  Widget build(BuildContext context) {
    final color    = _statusColor(_status);
    final stepIdx  = _currentStepIndex();
    final isDone   = _status == 'Delivered';
    final isPickup = _status == 'PickedUp';

    return Scaffold(
      backgroundColor: AppTheme.bg,
      appBar: AppBar(
        title: Text(widget.orderNumber),
        actions: [
          Container(
            margin: const EdgeInsets.only(left: 16),
            padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
            decoration: BoxDecoration(
              color: _connected ? AppTheme.secondary.withOpacity(0.15) : AppTheme.border,
              borderRadius: BorderRadius.circular(8),
            ),
            child: Row(mainAxisSize: MainAxisSize.min, children: [
              Container(width: 6, height: 6, decoration: BoxDecoration(
                shape: BoxShape.circle,
                color: _connected ? AppTheme.secondary : AppTheme.textSec)),
              const SizedBox(width: 4),
              Text(_connected ? 'مباشر' : 'جاري الاتصال...',
                style: TextStyle(
                  color: _connected ? AppTheme.secondary : AppTheme.textSec,
                  fontSize: 11)),
            ]),
          ),
        ],
      ),
      body: Column(children: [
        // ── Map View (simulated) ─────────────────────────────────────────
        Container(
          height: 220,
          color: const Color(0xFF0D1B2A),
          child: Stack(children: [
            // Map grid lines (simulated map)
            CustomPaint(
              size: const Size(double.infinity, 220),
              painter: _MapGridPainter(),
            ),
            // Driver dot
            if (isPickup || isDone)
              AnimatedBuilder(
                animation: _moveCtrl,
                builder: (_, __) => Positioned(
                  left: MediaQuery.of(context).size.width * 0.5 +
                    sin(_moveCtrl.value * 2 * pi) * 20,
                  top: 90 + cos(_moveCtrl.value * 2 * pi) * 20,
                  child: _DriverMarker(heading: _driverHeading, color: color),
                ),
              ),
            // Destination pin
            Positioned(
              right: MediaQuery.of(context).size.width * 0.3,
              top: 60,
              child: const _DestinationPin(),
            ),
            // Status overlay
            Positioned(
              bottom: 12, left: 12, right: 12,
              child: Container(
                padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
                decoration: BoxDecoration(
                  color: AppTheme.surface.withOpacity(0.95),
                  borderRadius: BorderRadius.circular(12),
                  border: Border.all(color: color.withOpacity(0.4)),
                ),
                child: Row(children: [
                  AnimatedBuilder(
                    animation: _pulseAnim,
                    builder: (_, child) => Transform.scale(
                      scale: isPickup ? _pulseAnim.value : 1.0,
                      child: child),
                    child: Text(_steps[stepIdx].$3,
                      style: const TextStyle(fontSize: 20)),
                  ),
                  const SizedBox(width: 10),
                  Expanded(child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start, children: [
                    Text(_statusMsg, style: TextStyle(
                      color: color, fontWeight: FontWeight.w700, fontSize: 13)),
                    if (isPickup)
                      const Text('الخريطة تعرض موقع السائق مباشرة',
                        style: TextStyle(color: AppTheme.textSec, fontSize: 10)),
                  ])),
                ]),
              ),
            ),
          ]),
        ),

        // ── Progress Steps ───────────────────────────────────────────────
        Expanded(child: SingleChildScrollView(
          padding: const EdgeInsets.all(20),
          child: Column(children: [

            // Step progress
            Container(
              padding: const EdgeInsets.all(16),
              decoration: BoxDecoration(color: AppTheme.card,
                borderRadius: BorderRadius.circular(16),
                border: Border.all(color: AppTheme.border)),
              child: Column(
                children: _steps.asMap().entries.map((e) {
                  final i     = e.key;
                  final step  = e.value;
                  final done  = i <= stepIdx;
                  final curr  = i == stepIdx;

                  return Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                    Column(children: [
                      Container(
                        width: 36, height: 36,
                        decoration: BoxDecoration(
                          shape: BoxShape.circle,
                          color: done ? step.$4.withOpacity(0.15) : AppTheme.surface,
                          border: Border.all(
                            color: done ? step.$4 : AppTheme.border,
                            width: curr ? 2.5 : 1)),
                        child: Center(child: Text(
                          done ? step.$3 : '○',
                          style: TextStyle(
                            fontSize: done ? 16 : 12,
                            color: done ? step.$4 : AppTheme.textSec))),
                      ),
                      if (i < _steps.length - 1)
                        Container(width: 2, height: 28,
                          color: i < stepIdx ? step.$4.withOpacity(0.4) : AppTheme.border),
                    ]),
                    const SizedBox(width: 14),
                    Expanded(child: Padding(
                      padding: const EdgeInsets.only(top: 6, bottom: 28),
                      child: Row(children: [
                        Expanded(child: Text(step.$2,
                          style: TextStyle(
                            color: curr ? step.$4
                              : done ? AppTheme.textPri : AppTheme.textSec,
                            fontWeight: curr || done
                              ? FontWeight.w700 : FontWeight.normal,
                            fontSize: 14))),
                        if (curr)
                          Container(
                            padding: const EdgeInsets.symmetric(
                              horizontal: 8, vertical: 2),
                            decoration: BoxDecoration(
                              color: step.$4.withOpacity(0.15),
                              borderRadius: BorderRadius.circular(8)),
                            child: Text('الآن', style: TextStyle(
                              color: step.$4, fontSize: 10,
                              fontWeight: FontWeight.w700))),
                      ]),
                    )),
                  ]);
                }).toList(),
              ),
            ),

            const SizedBox(height: 16),

            // Rate button when delivered
            if (isDone)
              ElevatedButton.icon(
                icon: const Icon(Icons.star_outline),
                label: const Text('قيّم تجربتك'),
                onPressed: () => Navigator.pop(context),
                style: ElevatedButton.styleFrom(
                  backgroundColor: const Color(0xFFF59E0B),
                  foregroundColor: Colors.black),
              ),
          ]),
        )),
      ]),
    );
  }
}

// ── Custom Painters ────────────────────────────────────────────────────────
class _MapGridPainter extends CustomPainter {
  @override
  void paint(Canvas canvas, Size size) {
    final paint = Paint()
      ..color = const Color(0xFF1E3A5F).withOpacity(0.6)
      ..strokeWidth = 1;

    // Grid lines
    for (double x = 0; x < size.width; x += 40)
      canvas.drawLine(Offset(x, 0), Offset(x, size.height), paint);
    for (double y = 0; y < size.height; y += 40)
      canvas.drawLine(Offset(0, y), Offset(size.width, y), paint);

    // Simulated roads
    final roadPaint = Paint()
      ..color = const Color(0xFF1E4080)
      ..strokeWidth = 4;
    canvas.drawLine(Offset(0, size.height * 0.5),
      Offset(size.width, size.height * 0.5), roadPaint);
    canvas.drawLine(Offset(size.width * 0.5, 0),
      Offset(size.width * 0.5, size.height), roadPaint);
  }

  @override
  bool shouldRepaint(covariant CustomPainter old) => false;
}

class _DriverMarker extends StatelessWidget {
  final double heading, color;
  const _DriverMarker({required this.heading, required this.color});

  @override
  Widget build(BuildContext context) => Transform.rotate(
    angle: heading * pi / 180,
    child: Container(
      width: 36, height: 36,
      decoration: BoxDecoration(
        shape: BoxShape.circle,
        color: AppTheme.primary,
        boxShadow: [BoxShadow(
          color: AppTheme.primary.withOpacity(0.4),
          blurRadius: 12, spreadRadius: 4)],
      ),
      child: const Icon(Icons.delivery_dining, color: Colors.white, size: 20)),
  );
}

class _DestinationPin extends StatelessWidget {
  const _DestinationPin();
  @override
  Widget build(BuildContext context) => Column(
    mainAxisSize: MainAxisSize.min, children: [
    Container(
      width: 32, height: 32,
      decoration: BoxDecoration(
        shape: BoxShape.circle, color: AppTheme.error,
        boxShadow: [BoxShadow(
          color: AppTheme.error.withOpacity(0.4),
          blurRadius: 8, spreadRadius: 2)],
      ),
      child: const Icon(Icons.home, color: Colors.white, size: 18)),
    Container(width: 2, height: 10, color: AppTheme.error),
  ]);
}
