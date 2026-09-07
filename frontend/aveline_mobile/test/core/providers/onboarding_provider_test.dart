import 'package:aveline_mobile/core/providers/onboarding_provider.dart';
import 'package:aveline_mobile/features/onboarding/data/onboarding_preferences.dart';
import 'package:aveline_mobile/features/onboarding/domain/account_type.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  late OnboardingProvider provider;

  setUp(() async {
    SharedPreferences.setMockInitialValues({});
    final prefs = await SharedPreferences.getInstance();
    provider = OnboardingProvider(OnboardingPreferences(prefs));
  });

  test('load returns null account type before any selection', () async {
    await provider.load('user_1');
    expect(provider.accountType, isNull);
    expect(provider.isOwner, isFalse);
  });

  test('select persists the account type and load restores it', () async {
    await provider.load('user_1');
    await provider.select(AccountType.owner);
    expect(provider.accountType, AccountType.owner);
    expect(provider.isOwner, isTrue);

    // A fresh provider reading the same storage sees the persisted choice.
    final prefs = await SharedPreferences.getInstance();
    final reloaded = OnboardingProvider(OnboardingPreferences(prefs));
    await reloaded.load('user_1');
    expect(reloaded.accountType, AccountType.owner);
  });

  test('account types are scoped per clerk id', () async {
    await provider.load('user_1');
    await provider.select(AccountType.staff);

    final prefs = await SharedPreferences.getInstance();
    final other = OnboardingProvider(OnboardingPreferences(prefs));
    await other.load('user_2');
    expect(other.accountType, isNull);
  });

  test('clear removes the persisted choice', () async {
    await provider.load('user_1');
    await provider.select(AccountType.owner);
    await provider.clear();
    expect(provider.accountType, isNull);

    final prefs = await SharedPreferences.getInstance();
    final reloaded = OnboardingProvider(OnboardingPreferences(prefs));
    await reloaded.load('user_1');
    expect(reloaded.accountType, isNull);
  });

  test('pending invite code is set and cleared', () async {
    await provider.load('user_1');
    expect(provider.pendingInviteCode, isNull);
    provider.setPendingInviteCode('ABC123');
    expect(provider.pendingInviteCode, 'ABC123');
    provider.setPendingInviteCode(null);
    expect(provider.pendingInviteCode, isNull);
  });
}
