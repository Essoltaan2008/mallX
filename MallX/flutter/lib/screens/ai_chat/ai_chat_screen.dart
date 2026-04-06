import 'dart:async';
import 'dart:convert';
import 'package:flutter/material.dart';
import 'package:http/http.dart' as http;
import '../../core/theme/app_theme.dart';
import '../../data/services/api_service.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

// ──────────────────────────────────────────────────────────────────────────
//  MODELS
// ──────────────────────────────────────────────────────────────────────────
class ChatMsg {
  final String  role;    // user | assistant
  String        content;
  bool          isStreaming;
  final DateTime time;

  ChatMsg({required this.role, required this.content,
    this.isStreaming = false}) : time = DateTime.now();

  bool get isUser => role == 'user';
}

class QuickReply {
  final String label, message;
  QuickReply({required this.label, required this.message});
  factory QuickReply.fromJson(Map j) =>
    QuickReply(label: j['label'] ?? '', message: j['message'] ?? '');
}

// ══════════════════════════════════════════════════════════════════════════
//  MALLX AI CHAT SCREEN
// ══════════════════════════════════════════════════════════════════════════
class MallAIChatScreen extends StatefulWidget {
  final String mallId;
  const MallAIChatScreen({super.key, required this.mallId});
  @override State<MallAIChatScreen> createState() => _MallAIChatScreenState();
}

class _MallAIChatScreenState extends State<MallAIChatScreen> {
  final _api      = ApiService();
  final _storage  = const FlutterSecureStorage();
  final _ctrl     = TextEditingController();
  final _scroll   = ScrollController();
  final _msgs     = <ChatMsg>[];
  List<QuickReply> _quickReplies = [];
  bool _typing    = false;
  bool _showQuick = true;

  static const _baseUrl = 'http://YOUR_SERVER_IP:5000';

  @override
  void initState() {
    super.initState();
    _loadQuickReplies();
    _addWelcome();
  }

  @override
  void dispose() { _ctrl.dispose(); _scroll.dispose(); super.dispose(); }

  void _addWelcome() {
    _msgs.add(ChatMsg(
      role: 'assistant',
      content: 'مرحباً! أنا مساعد MallX 🏬\n\n'
        'يسعدني مساعدتك في تصفح المحلات، تتبع طلباتك، معرفة نقاط ولائك، '
        'وكل ما تحتاجه داخل المول. كيف أقدر أساعدك؟',
    ));
  }

  Future<void> _loadQuickReplies() async {
    try {
      final res = await _api.get(
        '/mall/${widget.mallId}/ai/chat/quick-replies?lang=ar');
      setState(() {
        _quickReplies = (res.data['data'] as List? ?? [])
            .map((q) => QuickReply.fromJson(q)).toList();
      });
    } catch (_) {}
  }

  Future<void> _send(String text) async {
    if (text.trim().isEmpty) return;
    _ctrl.clear();
    setState(() {
      _msgs.add(ChatMsg(role: 'user', content: text));
      _typing    = true;
      _showQuick = false;
    });
    _scrollDown();

    // Build message history
    final history = _msgs
        .where((m) => !m.isStreaming)
        .map((m) => {'role': m.role, 'content': m.content})
        .toList();

    // Add streaming assistant message
    final assistantMsg = ChatMsg(
      role: 'assistant', content: '', isStreaming: true);
    setState(() => _msgs.add(assistantMsg));

    try {
      final token = await _storage.read(key: 'access_token');
      final req   = http.Request(
        'POST', Uri.parse('$_baseUrl/api/mall/${widget.mallId}/ai/chat/stream'));
      req.headers['Content-Type']  = 'application/json';
      req.headers['Authorization'] = 'Bearer $token';
      req.body = jsonEncode({'messages': history});

      final client   = http.Client();
      final response = await client.send(req);
      final stream   = response.stream.transform(utf8.decoder);

      await for (final chunk in stream) {
        for (final line in chunk.split('\n')) {
          if (!line.startsWith('data: ')) continue;
          final data = line.substring(6);
          if (data == '[DONE]') break;
          try {
            final json  = jsonDecode(data) as Map;
            final text  = json['text'] as String? ?? '';
            if (!mounted) break;
            setState(() {
              assistantMsg.content += text;
              assistantMsg.isStreaming = true;
            });
            _scrollDown();
          } catch (_) {}
        }
      }

      client.close();
      if (mounted) setState(() => assistantMsg.isStreaming = false);
    } catch (e) {
      if (mounted) {
        setState(() {
          assistantMsg.content = 'عذراً، حدث خطأ. حاول مرة أخرى. 🙏';
          assistantMsg.isStreaming = false;
        });
      }
    } finally {
      if (mounted) setState(() => _typing = false);
      _scrollDown();
    }
  }

  void _scrollDown() {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (_scroll.hasClients) {
        _scroll.animateTo(_scroll.position.maxScrollExtent,
          duration: const Duration(milliseconds: 300), curve: Curves.easeOut);
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Row(children: [
          CircleAvatar(
            radius: 16,
            backgroundColor: Color(0xFF1E3A5F),
            child: Text('🏬', style: TextStyle(fontSize: 14)),
          ),
          SizedBox(width: 10),
          Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Text('مساعد MallX', style: TextStyle(fontSize: 15, fontWeight: FontWeight.w700)),
            Text('متاح دائماً', style: TextStyle(fontSize: 11, color: AppTheme.secondary)),
          ]),
        ]),
        actions: [
          IconButton(
            icon: const Icon(Icons.refresh_outlined),
            onPressed: () {
              setState(() {
                _msgs.clear();
                _showQuick = true;
              });
              _addWelcome();
            }),
        ],
      ),
      body: Column(children: [

        // Messages
        Expanded(
          child: ListView.builder(
            controller:  _scroll,
            padding:     const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
            itemCount:   _msgs.length,
            itemBuilder: (_, i) => _MessageBubble(msg: _msgs[i]),
          ),
        ),

        // Typing indicator
        if (_typing && _msgs.last.content.isEmpty)
          const Padding(
            padding: EdgeInsets.only(right: 16, bottom: 8),
            child: Align(
              alignment: Alignment.centerRight,
              child: _TypingDots(),
            ),
          ),

        // Quick replies
        if (_showQuick && _quickReplies.isNotEmpty)
          SizedBox(
            height: 44,
            child: ListView.separated(
              scrollDirection: Axis.horizontal,
              padding: const EdgeInsets.symmetric(horizontal: 16),
              itemCount: _quickReplies.length,
              separatorBuilder: (_, __) => const SizedBox(width: 8),
              itemBuilder: (_, i) {
                final qr = _quickReplies[i];
                return GestureDetector(
                  onTap: () => _send(qr.message),
                  child: Container(
                    padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
                    decoration: BoxDecoration(
                      color: AppTheme.card,
                      borderRadius: BorderRadius.circular(20),
                      border: Border.all(color: AppTheme.primary.withOpacity(0.4)),
                    ),
                    child: Text(qr.label, style: const TextStyle(
                      color: AppTheme.primary, fontSize: 13, fontWeight: FontWeight.w600)),
                  ),
                );
              }),
          ),

        // Input
        Container(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 12),
          decoration: const BoxDecoration(
            color: AppTheme.surface,
            border: Border(top: BorderSide(color: AppTheme.border))),
          child: SafeArea(
            top: false,
            child: Row(children: [
              Expanded(
                child: TextField(
                  controller: _ctrl,
                  textInputAction: TextInputAction.send,
                  onSubmitted: _send,
                  maxLines: 4, minLines: 1,
                  decoration: InputDecoration(
                    hintText: 'اكتب سؤالك هنا...',
                    contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
                    border: OutlineInputBorder(
                      borderRadius: BorderRadius.circular(22),
                      borderSide: const BorderSide(color: AppTheme.border)),
                    enabledBorder: OutlineInputBorder(
                      borderRadius: BorderRadius.circular(22),
                      borderSide: const BorderSide(color: AppTheme.border)),
                    focusedBorder: OutlineInputBorder(
                      borderRadius: BorderRadius.circular(22),
                      borderSide: const BorderSide(color: AppTheme.primary, width: 2)),
                  ),
                ),
              ),
              const SizedBox(width: 8),
              GestureDetector(
                onTap: () => _send(_ctrl.text),
                child: Container(
                  width: 46, height: 46,
                  decoration: BoxDecoration(
                    color: _typing ? AppTheme.border : AppTheme.primary,
                    borderRadius: BorderRadius.circular(23)),
                  child: Icon(
                    _typing ? Icons.stop : Icons.send,
                    color: Colors.white, size: 20),
                ),
              ),
            ]),
          ),
        ),
      ]),
    );
  }
}

// ──────────────────────────────────────────────────────────────────────────
//  MESSAGE BUBBLE WIDGET
// ──────────────────────────────────────────────────────────────────────────
class _MessageBubble extends StatelessWidget {
  final ChatMsg msg;
  const _MessageBubble({required this.msg});

  @override
  Widget build(BuildContext context) {
    final isUser = msg.isUser;

    return Padding(
      padding: const EdgeInsets.only(bottom: 16),
      child: Row(
        mainAxisAlignment: isUser ? MainAxisAlignment.start : MainAxisAlignment.end,
        crossAxisAlignment: CrossAxisAlignment.end,
        children: [
          if (!isUser) ...[
            const CircleAvatar(
              radius: 16,
              backgroundColor: Color(0xFF1E3A5F),
              child: Text('🏬', style: TextStyle(fontSize: 12)),
            ),
            const SizedBox(width: 8),
          ],

          Flexible(
            child: Column(
              crossAxisAlignment: isUser
                  ? CrossAxisAlignment.start
                  : CrossAxisAlignment.end,
              children: [
                Container(
                  padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
                  constraints: BoxConstraints(
                    maxWidth: MediaQuery.of(context).size.width * 0.75),
                  decoration: BoxDecoration(
                    color: isUser ? AppTheme.primary : AppTheme.card,
                    borderRadius: BorderRadius.only(
                      topRight:    const Radius.circular(18),
                      topLeft:     const Radius.circular(18),
                      bottomRight: Radius.circular(isUser ? 4 : 18),
                      bottomLeft:  Radius.circular(isUser ? 18 : 4),
                    ),
                    border: isUser ? null : Border.all(color: AppTheme.border),
                  ),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        msg.content.isEmpty && msg.isStreaming ? '...' : msg.content,
                        style: TextStyle(
                          color: isUser ? Colors.white : AppTheme.textPri,
                          fontSize: 14, height: 1.5),
                      ),
                      if (msg.isStreaming && msg.content.isNotEmpty) ...[
                        const SizedBox(height: 6),
                        const _BlinkingCursor(),
                      ],
                    ],
                  ),
                ),
                const SizedBox(height: 4),
                Text(
                  '${msg.time.hour}:${msg.time.minute.toString().padLeft(2, "0")}',
                  style: const TextStyle(color: AppTheme.textSec, fontSize: 10)),
              ],
            ),
          ),

          if (isUser) ...[
            const SizedBox(width: 8),
            const CircleAvatar(
              radius: 16,
              backgroundColor: AppTheme.card,
              child: Icon(Icons.person, color: AppTheme.textSec, size: 18),
            ),
          ],
        ],
      ),
    );
  }
}

class _BlinkingCursor extends StatefulWidget {
  const _BlinkingCursor();
  @override State<_BlinkingCursor> createState() => _BlinkingCursorState();
}

class _BlinkingCursorState extends State<_BlinkingCursor>
    with SingleTickerProviderStateMixin {
  late AnimationController _ctrl;
  @override
  void initState() {
    super.initState();
    _ctrl = AnimationController(vsync: this, duration: const Duration(milliseconds: 600))
      ..repeat(reverse: true);
  }
  @override void dispose() { _ctrl.dispose(); super.dispose(); }
  @override
  Widget build(BuildContext context) => FadeTransition(
    opacity: _ctrl,
    child: Container(width: 2, height: 14, color: AppTheme.primary));
}

class _TypingDots extends StatefulWidget {
  const _TypingDots();
  @override State<_TypingDots> createState() => _TypingDotsState();
}

class _TypingDotsState extends State<_TypingDots>
    with TickerProviderStateMixin {
  final List<AnimationController> _ctrls = [];
  @override
  void initState() {
    super.initState();
    for (int i = 0; i < 3; i++) {
      final c = AnimationController(vsync: this, duration: const Duration(milliseconds: 400))
        ..repeat(reverse: true);
      Future.delayed(Duration(milliseconds: i * 150), () {
        if (mounted) c.forward();
      });
      _ctrls.add(c);
    }
  }
  @override void dispose() { for (final c in _ctrls) c.dispose(); super.dispose(); }
  @override
  Widget build(BuildContext context) => Container(
    padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
    decoration: BoxDecoration(
      color: AppTheme.card, borderRadius: BorderRadius.circular(18),
      border: Border.all(color: AppTheme.border)),
    child: Row(mainAxisSize: MainAxisSize.min, children: List.generate(3, (i) =>
      Padding(padding: const EdgeInsets.symmetric(horizontal: 3),
        child: FadeTransition(
          opacity: _ctrls[i],
          child: Container(width: 7, height: 7,
            decoration: const BoxDecoration(
              color: AppTheme.textSec, shape: BoxShape.circle)))))));
}
