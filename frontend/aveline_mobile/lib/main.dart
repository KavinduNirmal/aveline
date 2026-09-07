import 'package:flutter/material.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'app.dart';
import 'core/config/app_config.dart';
import 'features/onboarding/data/onboarding_preferences.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  final preferences = OnboardingPreferences(await SharedPreferences.getInstance());
  final config = AppConfig.fromEnvironment();
  runApp(AvelineApp(config: config, preferences: preferences));
}
