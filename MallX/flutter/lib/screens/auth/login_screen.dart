import 'package:flutter/material.dart';
import 'package:provider/provider.dart';
import '../../core/theme/app_theme.dart';
import '../../providers/providers.dart';
import '../home/main_nav_screen.dart';

// ══════════════════════════════════════════════════════════════════════════
//  LOGIN SCREEN
// ══════════════════════════════════════════════════════════════════════════
class LoginScreen extends StatefulWidget {
  const LoginScreen({super.key});
  @override State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen> {
  final _form     = GlobalKey<FormState>();
  final _email    = TextEditingController();
  final _password = TextEditingController();
  bool _obscure   = true;

  @override
  void dispose() {
    _email.dispose(); _password.dispose(); super.dispose();
  }

  Future<void> _login() async {
    if (!_form.currentState!.validate()) return;
    final auth  = context.read<AuthProvider>();
    final error = await auth.login(_email.text.trim(), _password.text);
    if (!mounted) return;
    if (error != null) {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(error), backgroundColor: AppTheme.error));
    } else {
      Navigator.pushReplacement(context,
        MaterialPageRoute(builder: (_) => const MainNavScreen()));
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: SafeArea(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(24),
          child: Form(
            key: _form,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                const SizedBox(height: 48),
                // Logo
                Container(
                  width: 80, height: 80,
                  decoration: BoxDecoration(
                    gradient: const LinearGradient(
                      colors: [AppTheme.primary, Color(0xFF8B5CF6)],
                      begin: Alignment.topLeft, end: Alignment.bottomRight,
                    ),
                    borderRadius: BorderRadius.circular(24),
                  ),
                  child: const Icon(Icons.shopping_bag_outlined,
                      color: Colors.white, size: 40),
                ),
                const SizedBox(height: 32),
                const Text('أهلاً بك في MallX',
                  style: TextStyle(fontSize: 28, fontWeight: FontWeight.w800,
                      color: AppTheme.textPri)),
                const SizedBox(height: 8),
                const Text('سجّل دخولك للاستمتاع بتجربة تسوق مميزة',
                  style: TextStyle(fontSize: 14, color: AppTheme.textSec)),
                const SizedBox(height: 40),
                // Email
                TextFormField(
                  controller: _email,
                  keyboardType: TextInputType.emailAddress,
                  textDirection: TextDirection.ltr,
                  decoration: const InputDecoration(
                    labelText: 'البريد الإلكتروني',
                    prefixIcon: Icon(Icons.email_outlined, color: AppTheme.textSec),
                  ),
                  validator: (v) {
                    if (v == null || v.isEmpty) return 'أدخل البريد الإلكتروني';
                    if (!v.contains('@')) return 'بريد غير صحيح';
                    return null;
                  },
                ),
                const SizedBox(height: 16),
                // Password
                TextFormField(
                  controller: _password,
                  obscureText: _obscure,
                  textDirection: TextDirection.ltr,
                  decoration: InputDecoration(
                    labelText: 'كلمة المرور',
                    prefixIcon: const Icon(Icons.lock_outlined, color: AppTheme.textSec),
                    suffixIcon: IconButton(
                      icon: Icon(_obscure ? Icons.visibility_off : Icons.visibility,
                          color: AppTheme.textSec),
                      onPressed: () => setState(() => _obscure = !_obscure),
                    ),
                  ),
                  validator: (v) {
                    if (v == null || v.length < 6) return 'كلمة المرور قصيرة جداً';
                    return null;
                  },
                ),
                const SizedBox(height: 32),
                // Button
                Consumer<AuthProvider>(
                  builder: (_, auth, __) => ElevatedButton(
                    onPressed: auth.isLoading ? null : _login,
                    child: auth.isLoading
                        ? const SizedBox(width: 20, height: 20,
                            child: CircularProgressIndicator(color: Colors.white, strokeWidth: 2))
                        : const Text('تسجيل الدخول'),
                  ),
                ),
                const SizedBox(height: 16),
                TextButton(
                  onPressed: () => Navigator.push(context,
                    MaterialPageRoute(builder: (_) => const RegisterScreen())),
                  child: const Text('ليس لديك حساب؟ سجّل الآن',
                    style: TextStyle(color: AppTheme.primary)),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

// ══════════════════════════════════════════════════════════════════════════
//  REGISTER SCREEN
// ══════════════════════════════════════════════════════════════════════════
class RegisterScreen extends StatefulWidget {
  const RegisterScreen({super.key});
  @override State<RegisterScreen> createState() => _RegisterScreenState();
}

class _RegisterScreenState extends State<RegisterScreen> {
  final _form      = GlobalKey<FormState>();
  final _firstName = TextEditingController();
  final _lastName  = TextEditingController();
  final _email     = TextEditingController();
  final _phone     = TextEditingController();
  final _password  = TextEditingController();
  final _confirm   = TextEditingController();
  bool _obscure    = true;

  @override
  void dispose() {
    for (final c in [_firstName, _lastName, _email, _phone, _password, _confirm]) {
      c.dispose();
    }
    super.dispose();
  }

  Future<void> _register() async {
    if (!_form.currentState!.validate()) return;
    final auth  = context.read<AuthProvider>();
    final error = await auth.register(
      firstName: _firstName.text.trim(),
      lastName:  _lastName.text.trim(),
      email:     _email.text.trim(),
      phone:     _phone.text.trim(),
      password:  _password.text,
    );
    if (!mounted) return;
    if (error != null) {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(error), backgroundColor: AppTheme.error));
    } else {
      Navigator.pushAndRemoveUntil(context,
        MaterialPageRoute(builder: (_) => const MainNavScreen()), (_) => false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('حساب جديد')),
      body: SingleChildScrollView(
        padding: const EdgeInsets.all(24),
        child: Form(
          key: _form,
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              _row([
                _field(_firstName, 'الاسم الأول', Icons.person_outlined),
                const SizedBox(width: 12),
                _field(_lastName, 'الاسم الأخير', Icons.person_outlined),
              ]),
              const SizedBox(height: 16),
              _emailField(),
              const SizedBox(height: 16),
              TextFormField(
                controller: _phone, keyboardType: TextInputType.phone,
                decoration: const InputDecoration(
                  labelText: 'رقم الجوال',
                  prefixIcon: Icon(Icons.phone_outlined, color: AppTheme.textSec),
                ),
                validator: (v) => (v == null || v.length < 10) ? 'رقم غير صحيح' : null,
              ),
              const SizedBox(height: 16),
              TextFormField(
                controller: _password, obscureText: _obscure,
                textDirection: TextDirection.ltr,
                decoration: InputDecoration(
                  labelText: 'كلمة المرور',
                  prefixIcon: const Icon(Icons.lock_outlined, color: AppTheme.textSec),
                  suffixIcon: IconButton(
                    icon: Icon(_obscure ? Icons.visibility_off : Icons.visibility,
                        color: AppTheme.textSec),
                    onPressed: () => setState(() => _obscure = !_obscure),
                  ),
                ),
                validator: (v) => (v == null || v.length < 8) ? '8 أحرف على الأقل' : null,
              ),
              const SizedBox(height: 16),
              TextFormField(
                controller: _confirm, obscureText: true,
                textDirection: TextDirection.ltr,
                decoration: const InputDecoration(
                  labelText: 'تأكيد كلمة المرور',
                  prefixIcon: Icon(Icons.lock_outlined, color: AppTheme.textSec),
                ),
                validator: (v) => v != _password.text ? 'كلمتا المرور غير متطابقتين' : null,
              ),
              const SizedBox(height: 32),
              Consumer<AuthProvider>(
                builder: (_, auth, __) => ElevatedButton(
                  onPressed: auth.isLoading ? null : _register,
                  child: auth.isLoading
                      ? const SizedBox(width: 20, height: 20,
                          child: CircularProgressIndicator(color: Colors.white, strokeWidth: 2))
                      : const Text('إنشاء الحساب'),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  Widget _row(List<Widget> children) =>
      Row(children: children.map((c) => c is SizedBox ? c : Expanded(child: c)).toList());

  Widget _field(TextEditingController c, String label, IconData icon) =>
      TextFormField(
        controller: c,
        decoration: InputDecoration(
          labelText: label,
          prefixIcon: Icon(icon, color: AppTheme.textSec),
        ),
        validator: (v) => (v == null || v.trim().isEmpty) ? 'مطلوب' : null,
      );

  Widget _emailField() => TextFormField(
    controller: _email, keyboardType: TextInputType.emailAddress,
    textDirection: TextDirection.ltr,
    decoration: const InputDecoration(
      labelText: 'البريد الإلكتروني',
      prefixIcon: Icon(Icons.email_outlined, color: AppTheme.textSec),
    ),
    validator: (v) {
      if (v == null || v.isEmpty) return 'أدخل البريد';
      if (!v.contains('@')) return 'بريد غير صحيح';
      return null;
    },
  );
}
