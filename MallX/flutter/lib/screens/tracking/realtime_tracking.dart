import 'dart:async';
import 'package:flutter/material.dart';
import 'package:signalr_netcore/signalr_client.dart';
import '../../core/theme/app_theme.dart';

// ──────────────────────────────────────────────────────────────────────────
//  REAL-TIME ORDER TRACKING WITH SIGNALR
// ──────────────────────────────────────────────────────────────────────────

const String _hubUrl = 'http://YOUR_SERVER_IP:5000/hubs/orders';

class RealtimeOrderTracker extends StatefulWidget {
  final String orderId;
  final String orderNumber;
  final String accessToken;

  const RealtimeOrderTracker({
    super.key,
    required this.orderId,
    required this.orderNumber,
    required this.accessToken,
  });

  @override
  State<RealtimeOrderTracker> createState() => _RealtimeOrderTrackerState();
}

class _RealtimeOrderTrackerState extends State<RealtimeOrderTracker> {
  HubConnection? _hub;
  String _currentStatus = 'Placed';
  String? _lastNote;
  bool _connected = false;
  bool _connecting = true;
  final List<_StatusEvent> _timeline = [];

  // Driver tracking
  double? _driverLat, _driverLng;
  bool _driverAssigned = false;

  @override
  void initState() {
    super.initState();
    _connectHub();
  }

  @override
  void dispose() {
    _hub?.stop();
    super.dispose();
  }

  Future<void> _connectHub() async {
    try {
      _hub = HubConnectionBuilder()
          .withUrl(
            '$_hubUrl?access_token=${widget.accessToken}',
            options: HttpConnectionOptions(
              transport: HttpTransportType.WebSockets,
              skipNegotiation: true,
            ),
          )
          .withAutomaticReconnect(retryDelays: [0, 2000, 5000, 10000, 30000])
          .build();

      // Listen for order status changes
      _hub!.on('OrderStatusChanged', (args) {
        if (!mounted) return;
        final data = args?[0] as Map<String, dynamic>?;
        if (data == null) return;
        setState(() {
          _currentStatus = data['newStatus'] ?? _currentStatus;
          _lastNote      = data['note'];
          _timeline.insert(0, _StatusEvent(
            status:    _currentStatus,
            note:      _lastNote,
            timestamp: DateTime.now(),
          ));
        });
      });

      // Listen for driver assignment
      _hub!.on('DriverAssigned', (args) {
        if (!mounted) return;
        setState(() => _driverAssigned = true);
        _connectDriverHub(args?[0]?['driverId'] as String?);
      });

      _hub!.onreconnecting(({error}) {
        if (mounted) setState(() => _connected = false);
      });
      _hub!.onreconnected(({connectionId}) {
        if (mounted) {
          setState(() => _connected = true);
          _joinOrderRoom();
        }
      });

      await _hub!.start();
      await _joinOrderRoom();

      if (mounted) setState(() { _connected = true; _connecting = false; });
    } catch (e) {
      if (mounted) setState(() => _connecting = false);
      debugPrint('SignalR connect error: $e');
    }
  }

  Future<void> _joinOrderRoom() async {
    await _hub?.invoke('JoinOrder', args: [widget.orderId]);
  }

  HubConnection? _driverHub;
  Future<void> _connectDriverHub(String? driverId) async {
    if (driverId == null) return;
    try {
      _driverHub = HubConnectionBuilder()
          .withUrl(
            'http://YOUR_SERVER_IP:5000/hubs/drivers?access_token=${widget.accessToken}',
            options: HttpConnectionOptions(
              transport: HttpTransportType.WebSockets,
              skipNegotiation: true,
            ),
          )
          .withAutomaticReconnect()
          .build();

      _driverHub!.on('DriverLocationUpdated', (args) {
        if (!mounted) return;
        final data = args?[0] as Map<String, dynamic>?;
        if (data == null) return;
        setState(() {
          _driverLat = (data['lat'] as num?)?.toDouble();
          _driverLng = (data['lng'] as num?)?.toDouble();
        });
      });

      await _driverHub!.start();
      await _driverHub!.invoke('TrackDriver', args: [driverId, widget.orderId]);
    } catch (e) {
      debugPrint('Driver hub error: $e');
    }
  }

  @override
  void dispose() {
    _hub?.stop();
    _driverHub?.stop();
    super.dispose();
  }

  static const _steps = [
    ('Placed',    'تم الاستلام',    Icons.check_circle_outline,  Color(0xFF64748B)),
    ('Confirmed', 'مؤكد',           Icons.thumb_up_outlined,      Color(0xFF3B82F6)),
    ('Preparing', 'قيد التحضير',   Icons.restaurant_outlined,    Color(0xFF8B5CF6)),
    ('Ready',     'جاهز',           Icons.done_all,               Color(0xFF10B981)),
    ('PickedUp',  'في الطريق',      Icons.delivery_dining,         Color(0xFFF59E0B)),
    ('Delivered', 'تم التسليم 🎉', Icons.home_outlined,          Color(0xFF10B981)),
  ];

  int _stepIndex() =>
    _steps.indexWhere((s) => s.$1 == _currentStatus).clamp(0, _steps.length - 1);

  @override
  Widget build(BuildContext context) {
    final stepIdx  = _stepIndex();
    final isDone   = _currentStatus == 'Delivered';
    final currStep = _steps[stepIdx];

    return Scaffold(
      appBar: AppBar(
        title: Text(widget.orderNumber),
        actions: [
          // Connection indicator
          Padding(
            padding: const EdgeInsets.all(16),
            child: Row(children: [
              Container(
                width: 8, height: 8,
                decoration: BoxDecoration(
                  shape: BoxShape.circle,
                  color: _connected ? AppTheme.secondary
                       : _connecting ? AppTheme.accent
                       : AppTheme.error,
                ),
              ),
              const SizedBox(width: 6),
              Text(
                _connected ? 'مباشر' : _connecting ? 'جاري...' : 'منقطع',
                style: TextStyle(
                  fontSize: 11,
                  color: _connected ? AppTheme.secondary : AppTheme.textSec),
              ),
            ]),
          ),
        ],
      ),
      body: SingleChildScrollView(
        padding: const EdgeInsets.all(20),
        child: Column(children: [

          // ── Status Hero ──────────────────────────────────────────────
          AnimatedContainer(
            duration: const Duration(milliseconds: 500),
            width: double.infinity,
            padding: const EdgeInsets.all(28),
            decoration: BoxDecoration(
              gradient: LinearGradient(
                colors: [
                  currStep.$4.withOpacity(0.25),
                  AppTheme.surface,
                ],
                begin: Alignment.topCenter, end: Alignment.bottomCenter,
              ),
              borderRadius: BorderRadius.circular(24),
              border: Border.all(color: currStep.$4.withOpacity(0.4), width: 1.5),
            ),
            child: Column(children: [
              TweenAnimationBuilder<double>(
                tween: Tween(begin: 0, end: 1),
                duration: const Duration(milliseconds: 600),
                builder: (_, v, child) => Transform.scale(scale: v, child: child),
                child: Icon(currStep.$3, color: currStep.$4, size: 56),
              ),
              const SizedBox(height: 14),
              Text(currStep.$2, style: TextStyle(
                color: currStep.$4, fontSize: 22, fontWeight: FontWeight.w900)),
              if (_lastNote != null) ...[
                const SizedBox(height: 8),
                Text(_lastNote!, style: const TextStyle(
                  color: AppTheme.textSec, fontSize: 13)),
              ],
              if (_driverAssigned && !isDone) ...[
                const SizedBox(height: 12),
                Container(
                  padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 6),
                  decoration: BoxDecoration(
                    color: AppTheme.accent.withOpacity(0.15),
                    borderRadius: BorderRadius.circular(12)),
                  child: Row(mainAxisSize: MainAxisSize.min, children: [
                    const Icon(Icons.delivery_dining, color: AppTheme.accent, size: 16),
                    const SizedBox(width: 6),
                    Text(
                      _driverLat != null
                        ? 'السائق قريب منك 📍'
                        : 'السائق في الطريق...',
                      style: const TextStyle(
                        color: AppTheme.accent, fontSize: 12, fontWeight: FontWeight.w700)),
                  ]),
                ),
              ],
            ]),
          ),

          const SizedBox(height: 28),

          // ── Progress Steps ────────────────────────────────────────────
          const Align(alignment: Alignment.centerRight,
            child: Text('مراحل الطلب', style: TextStyle(
              color: AppTheme.textPri, fontWeight: FontWeight.w700, fontSize: 16))),
          const SizedBox(height: 16),

          ..._steps.asMap().entries.map((e) {
            final i              = e.key;
            final (_, label, icon, color) = e.value;
            final done    = i <= stepIdx;
            final current = i == stepIdx;

            return Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Column(children: [
                AnimatedContainer(
                  duration: const Duration(milliseconds: 400),
                  width: 38, height: 38,
                  decoration: BoxDecoration(
                    color: done ? color : AppTheme.card,
                    shape: BoxShape.circle,
                    border: Border.all(
                      color: done ? color : AppTheme.border, width: 2),
                    boxShadow: current ? [
                      BoxShadow(color: color.withOpacity(0.4), blurRadius: 8, spreadRadius: 2)
                    ] : [],
                  ),
                  child: Icon(icon,
                    color: done ? Colors.white : AppTheme.textSec, size: 18),
                ),
                if (i < _steps.length - 1)
                  AnimatedContainer(
                    duration: const Duration(milliseconds: 400),
                    width: 2, height: 32,
                    color: i < stepIdx ? color : AppTheme.border),
              ]),
              const SizedBox(width: 14),
              Expanded(child: Padding(
                padding: const EdgeInsets.only(top: 8, bottom: 32),
                child: Row(children: [
                  Text(label, style: TextStyle(
                    color: current ? color
                         : done ? AppTheme.textPri : AppTheme.textSec,
                    fontWeight: current || done
                      ? FontWeight.w700 : FontWeight.normal,
                    fontSize: 14)),
                  if (current) ...[
                    const SizedBox(width: 8),
                    Container(
                      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
                      decoration: BoxDecoration(
                        color: color.withOpacity(0.15),
                        borderRadius: BorderRadius.circular(8)),
                      child: Text('الآن', style: TextStyle(
                        color: color, fontSize: 11, fontWeight: FontWeight.w700))),
                  ],
                ]),
              )),
            ]);
          }),

          // ── Live Timeline ─────────────────────────────────────────────
          if (_timeline.isNotEmpty) ...[
            const Divider(color: AppTheme.border),
            const SizedBox(height: 12),
            const Align(alignment: Alignment.centerRight,
              child: Text('التحديثات المباشرة', style: TextStyle(
                color: AppTheme.textSec, fontWeight: FontWeight.w600, fontSize: 14))),
            const SizedBox(height: 8),
            ..._timeline.take(5).map((ev) => Padding(
              padding: const EdgeInsets.only(bottom: 8),
              child: Row(children: [
                const Icon(Icons.circle, size: 6, color: AppTheme.primary),
                const SizedBox(width: 10),
                Expanded(child: Text(
                  ev.note ?? _statusAr(ev.status),
                  style: const TextStyle(color: AppTheme.textSec, fontSize: 12))),
                Text(
                  '${ev.timestamp.hour}:${ev.timestamp.minute.toString().padLeft(2,"0")}',
                  style: const TextStyle(color: AppTheme.textSec, fontSize: 11)),
              ]),
            )),
          ],

          if (isDone) ...[
            const SizedBox(height: 20),
            Container(
              padding: const EdgeInsets.all(16),
              decoration: BoxDecoration(
                color: AppTheme.secondary.withOpacity(0.1),
                borderRadius: BorderRadius.circular(14),
                border: Border.all(color: AppTheme.secondary.withOpacity(0.3))),
              child: const Row(children: [
                Icon(Icons.star_outline, color: AppTheme.secondary),
                SizedBox(width: 12),
                Expanded(child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start, children: [
                    Text('هل أعجبك طلبك؟', style: TextStyle(
                      color: AppTheme.textPri, fontWeight: FontWeight.w700)),
                    Text('قيّم تجربتك واكسب 5 نقاط ولاء!',
                      style: TextStyle(color: AppTheme.textSec, fontSize: 12)),
                ])),
                Icon(Icons.chevron_left, color: AppTheme.textSec),
              ]),
            ),
          ],
        ]),
      ),
    );
  }

  String _statusAr(String s) => switch (s) {
    'Placed'    => 'تم استلام الطلب',
    'Confirmed' => 'تم تأكيد الطلب',
    'Preparing' => 'جاري تحضير طلبك',
    'Ready'     => 'طلبك جاهز',
    'PickedUp'  => 'السائق استلم طلبك',
    'Delivered' => 'تم التسليم بنجاح',
    _ => s,
  };
}

class _StatusEvent {
  final String status;
  final String? note;
  final DateTime timestamp;
  _StatusEvent({required this.status, this.note, required this.timestamp});
}
