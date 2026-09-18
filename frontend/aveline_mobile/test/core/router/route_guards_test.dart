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

    test('moves a user whose profile failed to the connection screen', () {
      // Otherwise the guards would hold them on whichever onboarding screen
      // they were on, with no explanation and no way to retry.
      expect(
        RouteGuards.redirectForAuth(
          '/account-type',
          isSignedIn: true,
          hasCompletedOnboarding: false,
          profileFailed: true,
        ),
        '/connection',
      );
      expect(
        RouteGuards.redirectForAuth(
          '/onboarding',
          isSignedIn: true,
          hasCompletedOnboarding: true,
          accountType: 'owner',
          profileFailed: true,
        ),
        '/connection',
      );
    });

    test('leaves a user on the connection screen while the profile is unknown', () {
      expect(
        RouteGuards.redirectForAuth(
          '/connection',
          isSignedIn: true,
          profileFailed: true,
        ),
        isNull,
      );
    });

    test('leaves the connection screen once the profile loads', () {
      expect(
        RouteGuards.redirectForAuth(
          '/connection',
          isSignedIn: true,
          hasCompletedOnboarding: true,
          accountState: 'Active',
        ),
        '/',
      );
      expect(
        RouteGuards.redirectForAuth(
          '/connection',
          isSignedIn: true,
          hasCompletedOnboarding: true,
          accountState: 'OnboardingPending',
          accountType: 'staff',
        ),
        '/org-setup',
      );
    });

    test('sends a signed-out user on the connection screen back to sign in', () {
      expect(
        RouteGuards.redirectForAuth('/connection', isSignedIn: false),
        '/auth',
      );
    });
  });

  group('AppRoutes', () {
    test('serves every personal destination the side panel carries', () {
      expect(AppRoutes.notifications, '/notifications');
      expect(AppRoutes.settings, '/settings');
      expect(AppRoutes.conversations, '/conversations');
    });

    test('keeps the retired profile path declared', () {
      // The screen is gone - the account it showed became a section of Settings -
      // but the header's avatar and any stored link still name the old path, so
      // the router forwards it rather than answering with nothing.
      expect(AppRoutes.profile, '/profile');
      expect(AppRoutes.profile, isNot(AppRoutes.settings));
    });
  });
}
