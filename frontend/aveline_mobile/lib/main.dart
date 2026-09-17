import 'package:firebase_core/firebase_core.dart';
import 'package:flutter/material.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'app.dart';
import 'core/auth/clerk_session_persistor.dart';
import 'core/config/app_config.dart';
import 'features/onboarding/data/onboarding_preferences.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();

  // Firebase powers push notifications. Guarded so the app still runs when Firebase is
  // not configured (e.g. local dev / CI without google-services.json).
  try {
    await Firebase.initializeApp();
  } catch (_) {
    // Firebase unavailable; push notifications are disabled but the app continues.
  }

  final sharedPreferences = await SharedPreferences.getInstance();
  final preferences = OnboardingPreferences(sharedPreferences);
  final config = AppConfig.fromEnvironment();
  runApp(
    AvelineApp(
      config: config,
      preferences: preferences,
      sessionStore: ClerkSessionPersistor(sharedPreferences),
    ),
  );
}
