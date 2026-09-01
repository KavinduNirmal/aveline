import 'package:aveline_mobile/core/router/route_guards.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('RouteGuards.redirectForAuth', () {
    test('redirects unauthenticated users away from protected screens', () {
      expect(RouteGuards.redirectForAuth('/', isSignedIn: false), '/auth');
    });

    test('leaves unauthenticated users on the auth screen', () {
      expect(RouteGuards.redirectForAuth('/auth', isSignedIn: false), isNull);
    });

    test('lets authenticated users into protected screens', () {
      expect(RouteGuards.redirectForAuth('/', isSignedIn: true), isNull);
    });

    test('sends authenticated users away from the auth screen', () {
      expect(RouteGuards.redirectForAuth('/auth', isSignedIn: true), '/');
    });
  });
}
