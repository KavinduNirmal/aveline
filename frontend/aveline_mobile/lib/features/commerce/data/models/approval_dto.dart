import '../../domain/entities/approval_entry.dart';
import 'order_dto.dart';

class ApprovalQueueResponseDto {
  const ApprovalQueueResponseDto({
    required this.id,
    required this.organizationId,
    required this.orderId,
    required this.approvalType,
    required this.status,
    required this.thresholdExceeded,
    required this.reason,
    this.decisionComment,
    this.decidedBy,
    this.threadId,
    this.conversationId,
    required this.createdAt,
    this.decidedAt,
    this.order,
  });

  factory ApprovalQueueResponseDto.fromJson(Map<String, dynamic> json) {
    return ApprovalQueueResponseDto(
      id: (json['id'] ?? '') as String,
      organizationId: (json['organizationId'] ?? '') as String,
      orderId: (json['orderId'] ?? '') as String,
      approvalType: (json['approvalType'] ?? 'discount') as String,
      status: (json['status'] ?? 'pending') as String,
      thresholdExceeded: (json['thresholdExceeded'] as bool?) ?? false,
      reason: (json['reason'] ?? '') as String,
      decisionComment: json['decisionComment'] as String?,
      decidedBy: json['decidedBy'] as String?,
      threadId: json['threadId'] as String?,
      conversationId: json['conversationId'] as String?,
      createdAt: json['createdAt'] != null
          ? DateTime.tryParse(json['createdAt'] as String) ?? DateTime.now()
          : DateTime.now(),
      decidedAt: json['decidedAt'] != null
          ? DateTime.tryParse(json['decidedAt'] as String)
          : null,
      order: json['order'] != null && json['order'] is Map<String, dynamic>
          ? OrderResponseDto.fromJson(json['order'] as Map<String, dynamic>)
          : null,
    );
  }

  final String id;
  final String organizationId;
  final String orderId;
  final String approvalType;
  final String status;
  final bool thresholdExceeded;
  final String reason;
  final String? decisionComment;
  final String? decidedBy;
  final String? threadId;
  final String? conversationId;
  final DateTime createdAt;
  final DateTime? decidedAt;
  final OrderResponseDto? order;

  ApprovalEntry toDomain() {
    return ApprovalEntry(
      id: id,
      organizationId: organizationId,
      orderId: orderId,
      approvalType: approvalType,
      status: status,
      thresholdExceeded: thresholdExceeded,
      reason: reason,
      decisionComment: decisionComment,
      decidedBy: decidedBy,
      threadId: threadId,
      conversationId: conversationId,
      createdAt: createdAt,
      decidedAt: decidedAt,
      order: order?.toDomain(),
    );
  }
}
