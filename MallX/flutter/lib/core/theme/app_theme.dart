import 'package:flutter/material.dart';

class AppTheme {
  static const Color primary   = Color(0xFF3B82F6);
  static const Color secondary = Color(0xFF10B981);
  static const Color accent    = Color(0xFFF59E0B);
  static const Color error     = Color(0xFFEF4444);
  static const Color bg        = Color(0xFF0A0F1A);
  static const Color surface   = Color(0xFF0F172A);
  static const Color card      = Color(0xFF1E293B);
  static const Color border    = Color(0xFF334155);
  static const Color textPri   = Color(0xFFF1F5F9);
  static const Color textSec   = Color(0xFF94A3B8);

  static ThemeData get dark => ThemeData(
    useMaterial3: true,
    brightness: Brightness.dark,
    scaffoldBackgroundColor: bg,
    primaryColor: primary,
    fontFamily: 'Cairo',
    colorScheme: const ColorScheme.dark(
      primary: primary, secondary: secondary,
      surface: surface, error: error,
    ),
    appBarTheme: const AppBarTheme(
      backgroundColor: surface,
      foregroundColor: textPri,
      elevation: 0,
      centerTitle: true,
      titleTextStyle: TextStyle(
        fontFamily: 'Cairo', fontSize: 18,
        fontWeight: FontWeight.w700, color: textPri,
      ),
    ),
    cardTheme: CardTheme(
      color: card,
      elevation: 0,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(16),
        side: const BorderSide(color: border, width: 1),
      ),
    ),
    elevatedButtonTheme: ElevatedButtonThemeData(
      style: ElevatedButton.styleFrom(
        backgroundColor: primary,
        foregroundColor: Colors.white,
        minimumSize: const Size(double.infinity, 52),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14)),
        textStyle: const TextStyle(fontFamily: 'Cairo', fontSize: 16, fontWeight: FontWeight.w700),
      ),
    ),
    inputDecorationTheme: InputDecorationTheme(
      filled: true, fillColor: card,
      border: OutlineInputBorder(borderRadius: BorderRadius.circular(12), borderSide: const BorderSide(color: border)),
      enabledBorder: OutlineInputBorder(borderRadius: BorderRadius.circular(12), borderSide: const BorderSide(color: border)),
      focusedBorder: OutlineInputBorder(borderRadius: BorderRadius.circular(12), borderSide: const BorderSide(color: primary, width: 2)),
      labelStyle: const TextStyle(color: textSec),
      hintStyle: const TextStyle(color: textSec),
    ),
    bottomNavigationBarTheme: const BottomNavigationBarThemeData(
      backgroundColor: surface,
      selectedItemColor: primary,
      unselectedItemColor: textSec,
      type: BottomNavigationBarType.fixed,
      elevation: 0,
    ),
  );
}
