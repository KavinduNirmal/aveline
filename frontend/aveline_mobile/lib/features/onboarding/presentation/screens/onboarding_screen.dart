import 'package:aveline_mobile/core/providers/user_provider.dart';
import 'package:aveline_mobile/core/router/route_guards.dart';
import 'package:aveline_mobile/features/auth/domain/auth_repository.dart';
import 'package:aveline_mobile/features/auth/domain/aveline_user.dart';
import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

class OnboardingScreen extends StatefulWidget {
  const OnboardingScreen({super.key});

  @override
  State<OnboardingScreen> createState() => _OnboardingScreenState();
}

class _OnboardingScreenState extends State<OnboardingScreen> {
  final _formKey = GlobalKey<FormState>();
  late final TextEditingController _displayNameController;
  late final TextEditingController _phoneNumberController;
  late final TextEditingController _addressController;

  String _contactPreference = 'WhatsApp';
  bool _pushNotificationsEnabled = true;
  bool _isSubmitting = false;
  String? _errorMessage;

  @override
  void initState() {
    super.initState();
    final authRepo = context.read<AuthRepository?>();
    final currentUser = authRepo?.currentUser;

    final first = currentUser?.firstName;
    final last = currentUser?.lastName;
    final nameParts = <String>[];
    if (first != null && first.isNotEmpty) {
      nameParts.add(first);
    }
    if (last != null && last.isNotEmpty) {
      nameParts.add(last);
    }
    final initialName = nameParts.join(' ');

    _displayNameController = TextEditingController(text: initialName);
    _phoneNumberController = TextEditingController(text: '+94 ');
    _addressController = TextEditingController();
  }

  @override
  void dispose() {
    _displayNameController.dispose();
    _phoneNumberController.dispose();
    _addressController.dispose();
    super.dispose();
  }

  Future<void> _handleSubmit() async {
    if (!_formKey.currentState!.validate()) {
      return;
    }

    setState(() {
      _isSubmitting = true;
      _errorMessage = null;
    });

    try {
      final dio = context.read<Dio>();
      final userProvider = context.read<UserProvider>();
      final authRepo = context.read<AuthRepository?>();

      await userProvider.completeOnboarding(
        dio,
        displayName: _displayNameController.text.trim(),
        phoneNumber: _phoneNumberController.text.trim(),
        address: _addressController.text.trim(),
        profileImageUrl: authRepo?.currentUser?.imageUrl,
        contactPreference: _contactPreference,
        pushNotificationsEnabled: _pushNotificationsEnabled,
      );

      if (mounted) {
        final user = userProvider.user;
        final isActive = user?.accountState == AvelineAccountState.active;
        context.go(isActive ? AppRoutes.home : AppRoutes.orgSetup);
      }
    } catch (e) {
      if (mounted) {
        setState(() {
          _errorMessage = e.toString().replaceFirst('Exception: ', '');
        });
      }
    } finally {
      if (mounted) {
        setState(() {
          _isSubmitting = false;
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Scaffold(
      appBar: AppBar(
        title: Text(
          'AVELINE',
          style: theme.textTheme.labelMedium?.copyWith(
            letterSpacing: 2.0,
            color: scheme.primary,
          ),
        ),
        centerTitle: true,
      ),
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.symmetric(horizontal: 24.0, vertical: 16.0),
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 480),
              child: Form(
                key: _formKey,
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    Text(
                      'Complete Your Profile',
                      style: theme.textTheme.headlineMedium?.copyWith(
                        color: scheme.onSurface,
                      ),
                      textAlign: TextAlign.center,
                    ),
                    const SizedBox(height: 8),
                    Text(
                      'Welcome to Aveline AI. Confirm your details to enable personalized AI-powered boutique operations.',
                      style: theme.textTheme.bodyMedium?.copyWith(
                        color: scheme.onSurfaceVariant,
                      ),
                      textAlign: TextAlign.center,
                    ),
                    const SizedBox(height: 24),
                    if (_errorMessage != null) ...[
                      Container(
                        padding: const EdgeInsets.all(12),
                        decoration: BoxDecoration(
                          color: scheme.errorContainer,
                          borderRadius: BorderRadius.circular(12),
                          border: Border.all(color: scheme.error),
                        ),
                        child: Text(
                          _errorMessage!,
                          style: theme.textTheme.bodyMedium?.copyWith(
                            color: scheme.onErrorContainer,
                          ),
                        ),
                      ),
                      const SizedBox(height: 16),
                    ],
                    TextFormField(
                      controller: _displayNameController,
                      decoration: const InputDecoration(
                        labelText: 'Display Name *',
                        hintText: 'e.g. Kasun Delpachithra',
                        prefixIcon: Icon(Icons.person_outline),
                      ),
                      validator: (value) {
                        if (value == null || value.trim().isEmpty) {
                          return 'Please enter your display name';
                        }
                        return null;
                      },
                    ),
                    const SizedBox(height: 16),
                    TextFormField(
                      controller: _phoneNumberController,
                      keyboardType: TextInputType.phone,
                      decoration: const InputDecoration(
                        labelText: 'Contact Phone / WhatsApp *',
                        hintText: '+94 77 123 4567',
                        prefixIcon: Icon(Icons.phone_outlined),
                      ),
                      validator: (value) {
                        if (value == null ||
                            value.trim().isEmpty ||
                            value.trim() == '+94') {
                          return 'Please enter a valid contact phone number';
                        }
                        return null;
                      },
                    ),
                    const SizedBox(height: 16),
                    TextFormField(
                      controller: _addressController,
                      maxLines: 2,
                      decoration: const InputDecoration(
                        labelText: 'Boutique / Address *',
                        hintText: 'e.g. 15 Alfred House Gardens, Colombo 03',
                        prefixIcon: Icon(Icons.location_on_outlined),
                      ),
                      validator: (value) {
                        if (value == null || value.trim().isEmpty) {
                          return 'Please enter your boutique or delivery address';
                        }
                        return null;
                      },
                    ),
                    const SizedBox(height: 20),
                    Text(
                      'Preferred Concierge Channel',
                      style: theme.textTheme.labelMedium?.copyWith(
                        color: scheme.onSurfaceVariant,
                      ),
                    ),
                    const SizedBox(height: 8),
                    Wrap(
                      spacing: 8,
                      children: ['WhatsApp', 'SMS', 'Email', 'Phone'].map((channel) {
                        final isSelected = _contactPreference == channel;
                        return ChoiceChip(
                          label: Text(channel),
                          selected: isSelected,
                          onSelected: (selected) {
                            if (selected) {
                              setState(() {
                                _contactPreference = channel;
                              });
                            }
                          },
                        );
                      }).toList(),
                    ),
                    const SizedBox(height: 16),
                    SwitchListTile.adaptive(
                      contentPadding: EdgeInsets.zero,
                      title: Text(
                        'Push Notifications',
                        style: theme.textTheme.bodyMedium?.copyWith(
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                      subtitle: Text(
                        'Receive real-time order and sourcing alerts',
                        style: theme.textTheme.bodySmall?.copyWith(
                          color: scheme.onSurfaceVariant,
                        ),
                      ),
                      value: _pushNotificationsEnabled,
                      onChanged: (val) {
                        setState(() {
                          _pushNotificationsEnabled = val;
                        });
                      },
                    ),
                    const SizedBox(height: 28),
                    FilledButton(
                      onPressed: _isSubmitting ? null : _handleSubmit,
                      child: Padding(
                        padding: const EdgeInsets.symmetric(vertical: 14.0),
                        child: _isSubmitting
                            ? const SizedBox(
                                height: 20,
                                width: 20,
                                child: CircularProgressIndicator(
                                  strokeWidth: 2,
                                  color: Colors.white,
                                ),
                              )
                            : const Text('Complete Onboarding'),
                      ),
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
