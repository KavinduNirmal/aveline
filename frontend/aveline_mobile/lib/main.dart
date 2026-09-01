import 'package:flutter/material.dart';

import 'app.dart';
import 'core/config/app_config.dart';

void main() {
  final config = AppConfig.fromEnvironment();
  runApp(AvelineApp(config: config));
}
