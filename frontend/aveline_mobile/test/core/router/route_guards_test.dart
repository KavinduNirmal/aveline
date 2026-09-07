import 'package:aveline_mobile/core/router/route_guards.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('RouteGuards.redirectForAuth', () {
    test('redirects unauthenticated users away from protected screens', () {
      expect(RouteGuards.redirectForAuth('/', isSignedIn: false), '/auth');
      expect(RouteGuards.redirectForAuth('/onboarding', isSignedIn: false), '/auth');
      expect(RouteGuards.redirectForAuth('/org-setup', isSignedIn: false), '/auth');
      expect(
        RouteGuards.redirectForAuth('/account-type', isSignedIn: false),
        '/auth',
      );
    });

    test('leaves unauthenticated users on the auth screen', () {
      expect(RouteGuards.redirectForAuth('/auth', isSignedIn: false), isNull);
    });

    test('leaves unauthenticated users on the invite deep link', () {
      expect(RouteGuards.redirectForAuth('/invite', isSignedIn: false), isNull);
    });

    test('routes a pending user without a profile to account-type first', () {
      expect(
        RouteGuards.redirectForAuth('/', isSignedIn: true, hasCompletedOnboarding: false),
        '/account-type',
      );
      expect(
        RouteGuards.redirectForAuth(
          '/account-type',
          isSignedIn: true,
          hasCompletedOnboarding: false,
        ),
        isNull,
      );
    });

    test('routes a pending user with a chosen type to the profile step', () {
      expect(
        RouteGuards.redirectForAuth(
          '/',
          isSignedIn: true,
          hasCompletedOnboarding: false,
          accountType: 'owner',
        ),
        '/onboarding',
      );
      expect(
        RouteGuards.redirectForAuth(
          '/',
          isSignedIn: true,
          hasCompletedOnboarding: false,
          accountType: 'staff',
        ),
        '/onboarding',
      );
      expect(
        RouteGuards.redirectForAuth(
          '/onboarding',
          isSignedIn: true,
          hasCompletedOnboarding: false,
          accountType: 'owner',
        ),
        isNull,
      );
    });

    test('routes a pending owner with a completed profile to the owner wizard', () {
      expect(
        RouteGuards.redirectForAuth(
          '/',
          isSignedIn: true,
          hasCompletedOnboarding: true,
          accountState: 'OnboardingPending',
          accountType: 'owner',
        ),
        '/owner-onboarding',
      );
      expect(
        RouteGuards.redirectForAuth(
          '/owner-onboarding',
          isSignedIn: true,
          hasCompletedOnboarding: true,
          accountState: 'OnboardingPending',
          accountType: 'owner',
        ),
        isNull,
      );
    });

    test('routes a pending staff member to the invite-code step', () {
      expect(
        RouteGuards.redirectForAuth(
          '/',
          isSignedIn: true,
          hasCompletedOnboarding: true,
          accountState: 'OnboardingPending',
          accountType: 'staff',
        ),
        '/org-setup',
      );
      expect(
        RouteGuards.redirectForAuth(
          '/org-setup',
          isSignedIn: true,
          hasCompletedOnboarding: true,
          accountState: 'OnboardingPending',
          accountType: 'staff',
        ),
        isNull,
      );
    });

    test('defaults a pending user with an unknown type to the invite-code step', () {
      expect(
        RouteGuards.redirectForAuth(
          '/',
          isSignedIn: true,
          hasCompletedOnboarding: true,
          accountState: 'OnboardingPending',
        ),
        '/org-setup',
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
        RouteGuards.redirectForAuth(
          '/owner-onboarding',
          isSignedIn: true,
          hasCompletedOnboarding: true,
          accountState: 'Active',
        ),
        '/',
      );
      expect(RouteGuards.redirectForAuth('/auth', isSignedIn: true, accountState: 'Active'), '/');
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
