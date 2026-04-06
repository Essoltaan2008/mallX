import 'package:flutter/material.dart';
import '../../core/theme/app_theme.dart';
import '../../data/services/api_service.dart';

// ══════════════════════════════════════════════════════════════════════════
//  BOOKING SCREEN
// ══════════════════════════════════════════════════════════════════════════
class BookingScreen extends StatefulWidget {
  final String storeId, storeName;
  const BookingScreen({super.key, required this.storeId, required this.storeName});
  @override State<BookingScreen> createState() => _BookingScreenState();
}

class _BookingScreenState extends State<BookingScreen> {
  final _api    = ApiService();
  int   _step   = 0;             // 0=service 1=staff+date+time 2=confirm
  bool  _loading = true;

  List<Map>  _services     = [];
  List<Map>  _slots        = [];
  Map?       _selectedService;
  Map?       _selectedStaff;
  DateTime   _selectedDate = DateTime.now().add(const Duration(days: 1));
  Map?       _selectedSlot;
  bool       _booking      = false;
  String?    _successRef;

  @override
  void initState() { super.initState(); _loadServices(); }

  Future<void> _loadServices() async {
    try {
      final res = await _api.get('/mall/bookings/stores/${widget.storeId}/services');
      setState(() {
        _services = List<Map>.from(res.data['data'] ?? []);
        _loading  = false;
      });
    } catch (_) { setState(() => _loading = false); }
  }

  Future<void> _loadSlots() async {
    if (_selectedService == null) return;
    setState(() => _slots = []);
    try {
      final dateStr = '${_selectedDate.year}-${_selectedDate.month.toString().padLeft(2,"0")}-${_selectedDate.day.toString().padLeft(2,"0")}';
      final staffQ  = _selectedStaff != null ? '&staffId=${_selectedStaff!["id"]}' : '';
      final res = await _api.get(
        '/mall/bookings/stores/${widget.storeId}/availability?serviceId=${_selectedService!["id"]}&date=$dateStr$staffQ');
      setState(() {
        _slots = List<Map>.from((res.data['data']?['slots'] ?? [])
          .where((s) => s['isAvailable'] == true));
      });
    } catch (_) {}
  }

  Future<void> _book() async {
    if (_selectedSlot == null || _selectedService == null) return;
    setState(() => _booking = true);
    try {
      final dateStr = '${_selectedDate.year}-${_selectedDate.month.toString().padLeft(2,"0")}-${_selectedDate.day.toString().padLeft(2,"0")}';
      final res = await _api.post('/mall/bookings', data: {
        'storeId':   widget.storeId,
        'serviceId': _selectedService!['id'],
        'staffId':   _selectedStaff?['id'],
        'bookedDate': dateStr,
        'startTime': _selectedSlot!['startTime'],
      });
      setState(() => _successRef = res.data['data']['bookingRef']);
    } catch (e) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: const Text('تعذر إتمام الحجز. حاول مرة أخرى'),
          backgroundColor: AppTheme.error));
    }
    setState(() => _booking = false);
  }

  @override
  Widget build(BuildContext context) {
    if (_successRef != null) return _SuccessView(ref: _successRef!, storeName: widget.storeName);

    return Scaffold(
      appBar: AppBar(
        title: Text('حجز — ${widget.storeName}'),
        bottom: PreferredSize(
          preferredSize: const Size.fromHeight(4),
          child: LinearProgressIndicator(
            value: (_step + 1) / 3,
            backgroundColor: AppTheme.border,
            valueColor: const AlwaysStoppedAnimation(AppTheme.primary),
          ),
        ),
      ),
      body: _loading
        ? const Center(child: CircularProgressIndicator())
        : [_buildStep0(), _buildStep1(), _buildStep2()][_step],
    );
  }

  // Step 0: اختيار الخدمة
  Widget _buildStep0() => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      const Padding(padding: EdgeInsets.all(20),
        child: Text('اختر الخدمة', style: TextStyle(
          color: AppTheme.textPri, fontWeight: FontWeight.w800, fontSize: 18))),
      Expanded(child: ListView.separated(
        padding: const EdgeInsets.symmetric(horizontal: 16),
        itemCount: _services.length,
        separatorBuilder: (_, __) => const SizedBox(height: 10),
        itemBuilder: (_, i) {
          final s   = _services[i];
          final sel = _selectedService?['id'] == s['id'];
          return GestureDetector(
            onTap: () => setState(() {
              _selectedService = s; _selectedStaff = null; _selectedSlot = null;
            }),
            child: Container(
              padding: const EdgeInsets.all(16),
              decoration: BoxDecoration(
                color: sel ? AppTheme.primary.withOpacity(0.1) : AppTheme.card,
                borderRadius: BorderRadius.circular(14),
                border: Border.all(color: sel ? AppTheme.primary : AppTheme.border, width: sel ? 2 : 1)),
              child: Row(children: [
                Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                  Text(s['name'] ?? '', style: const TextStyle(
                    color: AppTheme.textPri, fontWeight: FontWeight.w700, fontSize: 15)),
                  if (s['description'] != null)
                    Text(s['description'], maxLines: 2,
                      style: const TextStyle(color: AppTheme.textSec, fontSize: 12)),
                  const SizedBox(height: 8),
                  Row(children: [
                    const Icon(Icons.timer_outlined, size: 14, color: AppTheme.textSec),
                    const SizedBox(width: 4),
                    Text('${s["durationMin"]} دقيقة',
                      style: const TextStyle(color: AppTheme.textSec, fontSize: 12)),
                    const SizedBox(width: 16),
                    Text('${s["price"]} ج.م',
                      style: const TextStyle(color: AppTheme.primary,
                        fontWeight: FontWeight.w700, fontSize: 14)),
                  ]),
                ])),
                if (sel) const Icon(Icons.check_circle, color: AppTheme.primary),
              ]),
            ),
          );
        },
      )),
      Padding(padding: const EdgeInsets.all(16),
        child: ElevatedButton(
          onPressed: _selectedService == null ? null : () {
            _loadSlots();
            setState(() => _step = 1);
          },
          child: const Text('التالي — اختر الموعد'),
        )),
    ],
  );

  // Step 1: تاريخ + وقت
  Widget _buildStep1() => Column(children: [
    // Date picker
    Container(
      margin: const EdgeInsets.all(16),
      decoration: BoxDecoration(color: AppTheme.card, borderRadius: BorderRadius.circular(14),
        border: Border.all(color: AppTheme.border)),
      child: CalendarDatePicker(
        initialDate: _selectedDate,
        firstDate: DateTime.now().add(const Duration(hours: 2)),
        lastDate: DateTime.now().add(const Duration(days: 30)),
        onDateChanged: (d) { setState(() { _selectedDate = d; _selectedSlot = null; }); _loadSlots(); },
      ),
    ),
    // Staff filter
    if ((_selectedService?['availableStaff'] as List?)?.isNotEmpty == true)
      SingleChildScrollView(
        scrollDirection: Axis.horizontal,
        padding: const EdgeInsets.symmetric(horizontal: 16),
        child: Row(children: [
          _staffChip(null, 'أي موظف'),
          ...(_selectedService!['availableStaff'] as List).map((s) =>
            _staffChip(s, s['name'])),
        ]),
      ),
    const SizedBox(height: 12),
    // Slots
    if (_slots.isEmpty)
      const Expanded(child: Center(child: Text('لا توجد مواعيد متاحة في هذا اليوم',
        style: TextStyle(color: AppTheme.textSec))))
    else
      Expanded(child: GridView.builder(
        padding: const EdgeInsets.symmetric(horizontal: 16),
        gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
          crossAxisCount: 4, mainAxisSpacing: 8, crossAxisSpacing: 8, childAspectRatio: 2),
        itemCount: _slots.length,
        itemBuilder: (_, i) {
          final slot = _slots[i];
          final sel  = _selectedSlot == slot;
          return GestureDetector(
            onTap: () => setState(() => _selectedSlot = slot),
            child: Container(
              alignment: Alignment.center,
              decoration: BoxDecoration(
                color: sel ? AppTheme.primary : AppTheme.card,
                borderRadius: BorderRadius.circular(8),
                border: Border.all(color: sel ? AppTheme.primary : AppTheme.border)),
              child: Text(slot['startTime'].toString().substring(0, 5),
                style: TextStyle(color: sel ? Colors.white : AppTheme.textPri,
                  fontSize: 12, fontWeight: FontWeight.w600)),
            ),
          );
        },
      )),
    Padding(padding: const EdgeInsets.all(16),
      child: Row(children: [
        Expanded(child: OutlinedButton(
          onPressed: () => setState(() => _step = 0),
          style: OutlinedButton.styleFrom(foregroundColor: AppTheme.textSec,
            side: const BorderSide(color: AppTheme.border)),
          child: const Text('رجوع'))),
        const SizedBox(width: 12),
        Expanded(child: ElevatedButton(
          onPressed: _selectedSlot == null ? null : () => setState(() => _step = 2),
          child: const Text('التالي'))),
      ])),
  ]);

  // Step 2: تأكيد
  Widget _buildStep2() => Padding(
    padding: const EdgeInsets.all(20),
    child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
      const Text('تأكيد الحجز', style: TextStyle(
        color: AppTheme.textPri, fontWeight: FontWeight.w800, fontSize: 18)),
      const SizedBox(height: 20),
      _confirmRow('الخدمة',  _selectedService?['name'] ?? ''),
      _confirmRow('المحل',   widget.storeName),
      _confirmRow('التاريخ', '${_selectedDate.day}/${_selectedDate.month}/${_selectedDate.year}'),
      _confirmRow('الوقت',   _selectedSlot?['startTime']?.toString().substring(0,5) ?? ''),
      _confirmRow('السعر',   '${_selectedService?["price"] ?? 0} ج.م'),
      if (_selectedStaff != null) _confirmRow('الموظف', _selectedStaff!['name']),
      const Spacer(),
      ElevatedButton(
        onPressed: _booking ? null : _book,
        child: _booking
          ? const SizedBox(width: 20, height: 20,
              child: CircularProgressIndicator(color: Colors.white, strokeWidth: 2))
          : const Text('تأكيد الحجز نهائياً'),
      ),
      const SizedBox(height: 8),
      OutlinedButton(
        onPressed: () => setState(() => _step = 1),
        style: OutlinedButton.styleFrom(foregroundColor: AppTheme.textSec,
          side: const BorderSide(color: AppTheme.border)),
        child: const Text('رجوع'),
      ),
    ]),
  );

  Widget _confirmRow(String label, String value) => Padding(
    padding: const EdgeInsets.only(bottom: 12),
    child: Row(children: [
      SizedBox(width: 80, child: Text(label, style: const TextStyle(color: AppTheme.textSec))),
      Text(value, style: const TextStyle(color: AppTheme.textPri, fontWeight: FontWeight.w600)),
    ]),
  );

  Widget _staffChip(Map? staff, String label) => GestureDetector(
    onTap: () { setState(() { _selectedStaff = staff; _selectedSlot = null; }); _loadSlots(); },
    child: Container(
      margin: const EdgeInsets.only(left: 8),
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 6),
      decoration: BoxDecoration(
        color: _selectedStaff == staff ? AppTheme.primary : AppTheme.card,
        borderRadius: BorderRadius.circular(20),
        border: Border.all(color: _selectedStaff == staff ? AppTheme.primary : AppTheme.border)),
      child: Text(label, style: TextStyle(
        color: _selectedStaff == staff ? Colors.white : AppTheme.textSec,
        fontSize: 12, fontWeight: FontWeight.w600)),
    ),
  );
}

class _SuccessView extends StatelessWidget {
  final String ref, storeName;
  const _SuccessView({required this.ref, required this.storeName});

  @override
  Widget build(BuildContext context) => Scaffold(
    body: Center(child: Padding(
      padding: const EdgeInsets.all(32),
      child: Column(mainAxisAlignment: MainAxisAlignment.center, children: [
        Container(width: 96, height: 96,
          decoration: BoxDecoration(color: AppTheme.secondary.withOpacity(0.15),
            shape: BoxShape.circle),
          child: const Icon(Icons.check_circle, color: AppTheme.secondary, size: 56)),
        const SizedBox(height: 24),
        const Text('تم الحجز بنجاح! 🎉', style: TextStyle(
          color: AppTheme.textPri, fontSize: 22, fontWeight: FontWeight.w800)),
        const SizedBox(height: 8),
        Text('رقم الحجز: $ref', style: const TextStyle(
          color: AppTheme.primary, fontSize: 16, fontWeight: FontWeight.w700)),
        const SizedBox(height: 8),
        Text(storeName, style: const TextStyle(color: AppTheme.textSec)),
        const SizedBox(height: 32),
        ElevatedButton(
          onPressed: () => Navigator.popUntil(context, (r) => r.isFirst),
          child: const Text('العودة للرئيسية')),
      ]),
    )),
  );
}

// ══════════════════════════════════════════════════════════════════════════
//  RATING SCREEN
// ══════════════════════════════════════════════════════════════════════════
class RatingScreen extends StatefulWidget {
  final String mallOrderId, storeId, storeName;
  const RatingScreen({super.key, required this.mallOrderId,
    required this.storeId, required this.storeName});
  @override State<RatingScreen> createState() => _RatingScreenState();
}

class _RatingScreenState extends State<RatingScreen> {
  final _api       = ApiService();
  int   _stars     = 0;
  final _titleCtrl = TextEditingController();
  final _bodyCtrl  = TextEditingController();
  bool  _anon      = false;
  bool  _submitting = false;
  bool  _done      = false;

  Future<void> _submit() async {
    if (_stars == 0) {
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(
        content: Text('اختر عدد النجوم أولاً')));
      return;
    }
    setState(() => _submitting = true);
    try {
      await _api.post('/mall/ratings', data: {
        'mallOrderId': widget.mallOrderId,
        'storeId':     widget.storeId,
        'subject':     'Store',
        'stars':       _stars,
        'title':       _titleCtrl.text.trim().isEmpty ? null : _titleCtrl.text.trim(),
        'body':        _bodyCtrl.text.trim().isEmpty ? null : _bodyCtrl.text.trim(),
        'isAnonymous': _anon,
      });
      setState(() => _done = true);
    } catch (_) {
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(
        content: Text('تعذر إرسال التقييم. حاول مرة أخرى'),
        backgroundColor: AppTheme.error));
    }
    setState(() => _submitting = false);
  }

  @override
  Widget build(BuildContext context) {
    if (_done) return Scaffold(body: Center(child: Column(
      mainAxisAlignment: MainAxisAlignment.center, children: [
        const Icon(Icons.star, color: Color(0xFFF59E0B), size: 72),
        const SizedBox(height: 16),
        const Text('شكراً على تقييمك! 🌟', style: TextStyle(
          color: AppTheme.textPri, fontSize: 20, fontWeight: FontWeight.w800)),
        const SizedBox(height: 8),
        const Text('حصلت على 5 نقاط ولاء 🎁',
          style: TextStyle(color: AppTheme.secondary)),
        const SizedBox(height: 32),
        ElevatedButton(
          onPressed: () => Navigator.pop(context),
          child: const Text('إغلاق')),
      ])));

    return Scaffold(
      appBar: AppBar(title: Text('تقييم ${widget.storeName}')),
      body: SingleChildScrollView(
        padding: const EdgeInsets.all(24),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          // Stars
          Center(child: Column(children: [
            const Text('كيف كانت تجربتك؟', style: TextStyle(
              color: AppTheme.textPri, fontSize: 18, fontWeight: FontWeight.w700)),
            const SizedBox(height: 16),
            Row(mainAxisAlignment: MainAxisAlignment.center,
              children: List.generate(5, (i) => GestureDetector(
                onTap: () => setState(() => _stars = i + 1),
                child: Padding(padding: const EdgeInsets.symmetric(horizontal: 6),
                  child: Icon(i < _stars ? Icons.star : Icons.star_outline,
                    color: const Color(0xFFF59E0B), size: 44)),
              ))),
            const SizedBox(height: 8),
            Text(
              ['', 'سيئ جداً', 'سيئ', 'مقبول', 'جيد', 'ممتاز!'][_stars],
              style: TextStyle(
                color: _stars > 0 ? const Color(0xFFF59E0B) : AppTheme.textSec,
                fontWeight: FontWeight.w700, fontSize: 16)),
          ])),

          const SizedBox(height: 32),

          const Text('عنوان التقييم (اختياري)',
            style: TextStyle(color: AppTheme.textSec, fontSize: 13)),
          const SizedBox(height: 8),
          TextField(controller: _titleCtrl,
            decoration: const InputDecoration(hintText: 'مثال: خدمة رائعة!')),

          const SizedBox(height: 16),

          const Text('رأيك التفصيلي (اختياري)',
            style: TextStyle(color: AppTheme.textSec, fontSize: 13)),
          const SizedBox(height: 8),
          TextField(controller: _bodyCtrl, maxLines: 4,
            decoration: const InputDecoration(
              hintText: 'شاركنا تجربتك بالتفصيل...')),

          const SizedBox(height: 16),

          // Anonymous toggle
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 4),
            decoration: BoxDecoration(color: AppTheme.card,
              borderRadius: BorderRadius.circular(12),
              border: Border.all(color: AppTheme.border)),
            child: Row(children: [
              const Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text('تقييم مجهول', style: TextStyle(
                  color: AppTheme.textPri, fontWeight: FontWeight.w600)),
                Text('لن يظهر اسمك مع التقييم',
                  style: TextStyle(color: AppTheme.textSec, fontSize: 12)),
              ])),
              Switch(value: _anon, onChanged: (v) => setState(() => _anon = v),
                activeColor: AppTheme.primary),
            ]),
          ),

          const SizedBox(height: 12),
          Container(padding: const EdgeInsets.all(12),
            decoration: BoxDecoration(color: AppTheme.secondary.withOpacity(0.1),
              borderRadius: BorderRadius.circular(10)),
            child: const Row(children: [
              Icon(Icons.stars, color: AppTheme.secondary, size: 18),
              SizedBox(width: 8),
              Text('ستحصل على 5 نقاط ولاء مقابل تقييمك',
                style: TextStyle(color: AppTheme.secondary, fontSize: 12,
                  fontWeight: FontWeight.w600)),
            ])),

          const SizedBox(height: 28),

          ElevatedButton(
            onPressed: _submitting ? null : _submit,
            child: _submitting
              ? const SizedBox(width: 20, height: 20,
                  child: CircularProgressIndicator(color: Colors.white, strokeWidth: 2))
              : const Text('إرسال التقييم'),
          ),
        ]),
      ),
    );
  }
}
