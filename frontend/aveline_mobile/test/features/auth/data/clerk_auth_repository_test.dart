import 'package:aveline_mobile/features/auth/data/clerk_auth_repository.dart';
import 'package:clerk_auth/clerk_auth.dart' as clerk;
import 'package:clerk_auth/src/models/api/external_error.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('ClerkAuthRepository.extractErrorMessage', () {
    test('extracts clean message from ClerkError argument when present', () {
      const error = clerk.ClerkError(
        code: clerk.ClerkErrorCode.serverErrorResponse,
        message: '{arg} (ERROR RECEIVED FROM SERVER)',
        argument: "Couldn't find your account.",
      );

      final message = ClerkAuthRepository.extractErrorMessage(error);
      expect(message, "Couldn't find your account.");
      expect(message, isNot(contains('{arg}')));
      expect(message, isNot(contains('ERROR RECEIVED FROM SERVER')));
    });

    test('extracts message from ExternalErrorCollection when present', () {
      const error = clerk.ClerkError(
        code: clerk.ClerkErrorCode.serverErrorResponse,
        message: '{arg} (ERROR RECEIVED FROM SERVER)',
        errors: ExternalErrorCollection(
          errors: [
            ExternalError(
              message: 'Password is incorrect',
              longMessage: 'The password you entered is incorrect.',
              code: 'form_password_incorrect',
            ),
          ],
        ),
      );

      final message = ClerkAuthRepository.extractErrorMessage(error);
      expect(message, 'The password you entered is incorrect.');
      expect(message, isNot(contains('{arg}')));
    });

    test('falls back gracefully when error contains no argument or details', () {
      const error = clerk.ClerkError(
        code: clerk.ClerkErrorCode.serverErrorResponse,
        message: '{arg} (ERROR RECEIVED FROM SERVER)',
      );

      final message = ClerkAuthRepository.extractErrorMessage(error);
      expect(message, isNotEmpty);
      expect(message, isNot(contains('{arg}')));
      expect(message, 'Authentication failed. Please verify your credentials and try again.');
    });

    test('formats client app errors correctly', () {
      final error = clerk.ClerkError.clientAppError(message: 'Network timeout');

      final message = ClerkAuthRepository.extractErrorMessage(error);
      expect(message, 'Network timeout');
    });
  });
}
