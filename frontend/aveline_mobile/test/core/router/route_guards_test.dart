import 'package:aveline_mobile/core/router/route_guards.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('RouteGuards.redirectForAuth', () {
    test('redirects unauthenticated users away from protected screens', () {
      expect(RouteGuards.redirectForAuth('/', isSignedIn: false), '/auth');
      expect(RouteGuards.redirectForAuth('/onboarding', isSignedIn: false), '/auth');
    });

    test('leaves unauthenticated users on the auth screen', () {
      expect(RouteGuards.redirectForAuth('/auth', isSignedIn: false), isNull);
    });

    test('redirects authenticated user to /onboarding if not yet onboarded', () {
      expect(
        RouteGuards.redirectForAuth('/', isSignedIn: true, hasCompletedOnboarding: false),
        '/onboarding',
      );
      expect(
        RouteGuards.redirectForAuth('/auth', isSignedIn: true, hasCompletedOnboarding: false),
        '/onboarding',
      );
      expect(
        RouteGuards.redirectForAuth('/onboarding', isSignedIn: true, hasCompletedOnboarding: false),
        isNull,
      );
    });

    test('lets onboarded authenticated users into protected screens', () {
      expect(
        RouteGuards.redirectForAuth('/', isSignedIn: true, hasCompletedOnboarding: true),
        isNull,
      );
    });

    test('redirects onboarded authenticated users away from auth and onboarding screens', () {
      expect(
        RouteGuards.redirectForAuth('/auth', isSignedIn: true, hasCompletedOnboarding: true),
        '/',
      );
      expect(
        RouteGuards.redirectForAuth('/onboarding', isSignedIn: true, hasCompletedOnboarding: true),
        '/',
      );
    });
  });
}
