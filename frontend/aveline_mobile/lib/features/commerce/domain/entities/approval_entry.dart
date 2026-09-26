import 'order.dart';

class ApprovalEntry {
  const ApprovalEntry({
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

  final String id;
  final String organizationId;
  final String orderId;
  final String approvalType;
  final String status; // pending, approved, rejected, revised
  final bool thresholdExceeded;
  final String reason;
  final String? decisionComment;
  final String? decidedBy;
  final String? threadId;
  final String? conversationId;
  final DateTime createdAt;
  final DateTime? decidedAt;
  final Order? order;

  bool get isPending => status.toLowerCase() == 'pending';
  bool get isApproved => status.toLowerCase() == 'approved';
  bool get isRejected => status.toLowerCase() == 'rejected';
  bool get isRevised => status.toLowerCase() == 'revised';
}
