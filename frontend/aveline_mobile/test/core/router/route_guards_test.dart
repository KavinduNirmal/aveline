import 'package:aveline_mobile/core/router/route_guards.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('RouteGuards.redirectForAuth', () {
    test('redirects unauthenticated users away from protected screens', () {
      expect(RouteGuards.redirectForAuth('/', isSignedIn: false), '/auth');
      expect(RouteGuards.redirectForAuth('/onboarding', isSignedIn: false), '/auth');
      expect(RouteGuards.redirectForAuth('/org-setup', isSignedIn: false), '/auth');
    });

    test('leaves unauthenticated users on the auth screen', () {
      expect(RouteGuards.redirectForAuth('/auth', isSignedIn: false), isNull);
    });

    test('redirects authenticated user to /onboarding if profile not completed', () {
      expect(
        RouteGuards.redirectForAuth('/', isSignedIn: true, hasCompletedOnboarding: false),
        '/onboarding',
      );
      expect(
        RouteGuards.redirectForAuth('/auth', isSignedIn: true, hasCompletedOnboarding: false),
        '/onboarding',
      );
      expect(
        RouteGuards.redirectForAuth('/org-setup', isSignedIn: true, hasCompletedOnboarding: false),
        '/onboarding',
      );
      expect(
        RouteGuards.redirectForAuth('/onboarding', isSignedIn: true, hasCompletedOnboarding: false),
        isNull,
      );
    });

    test('redirects a pending user with a completed profile to /org-setup', () {
      expect(
        RouteGuards.redirectForAuth(
          '/',
          isSignedIn: true,
          hasCompletedOnboarding: true,
          accountState: 'OnboardingPending',
        ),
        '/org-setup',
      );
      expect(
        RouteGuards.redirectForAuth(
          '/org-setup',
          isSignedIn: true,
          hasCompletedOnboarding: true,
          accountState: 'OnboardingPending',
        ),
        isNull,
      );
    });

    test('redirects an active user away from onboarding and setup screens', () {
      expect(
        RouteGuards.redirectForAuth('/', isSignedIn: true, accountState: 'Active'),
        isNull,
      );
      expect(
        RouteGuards.redirectForAuth(
          '/onboarding',
          isSignedIn: true,
          hasCompletedOnboarding: true,
          accountState: 'Active',
        ),
        '/',
      );
      expect(
        RouteGuards.redirectForAuth(
          '/org-setup',
          isSignedIn: true,
          hasCompletedOnboarding: true,
          accountState: 'Active',
        ),
        '/',
      );
      expect(
        RouteGuards.redirectForAuth('/auth', isSignedIn: true, accountState: 'Active'),
        '/',
      );
    });

    test('redirects a suspended user to /suspended and leaves them there', () {
      expect(
        RouteGuards.redirectForAuth('/', isSignedIn: true, accountState: 'Suspended'),
        '/suspended',
      );
      expect(
        RouteGuards.redirectForAuth(
          '/suspended',
          isSignedIn: true,
          hasCompletedOnboarding: true,
          accountState: 'Suspended',
        ),
        isNull,
      );
    });

    test('keeps legacy behavior when account state is still loading', () {
      expect(
        RouteGuards.redirectForAuth('/', isSignedIn: true, hasCompletedOnboarding: true),
        isNull,
      );
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
