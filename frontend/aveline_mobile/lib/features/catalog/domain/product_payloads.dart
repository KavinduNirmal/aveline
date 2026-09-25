/// Staged image upload response from the backend media store.
class ImageUploadResult {
  const ImageUploadResult({
    required this.id,
    required this.url,
    this.fileName,
    this.fileSizeBytes,
    this.contentType,
  });

  final String id;
  final String url;
  final String? fileName;
  final int? fileSizeBytes;
  final String? contentType;

  factory ImageUploadResult.fromJson(Map<String, dynamic> json) {
    return ImageUploadResult(
      id: json['id']?.toString() ?? '',
      url: json['url']?.toString() ?? '',
      fileName: json['fileName']?.toString(),
      fileSizeBytes: (json['size'] as num?)?.toInt() ?? (json['fileSizeBytes'] as num?)?.toInt(),
      contentType: json['contentType']?.toString(),
    );
  }
}

/// Payload sent to `POST /api/v1/orgs/{orgId}/catalog/items` to create a new piece.
class CreateProductPayload {
  const CreateProductPayload({
    required this.name,
    required this.category,
    required this.color,
    required this.sizes,
    required this.price,
    this.cost = 0,
    this.quantity = 1,
    this.colorHex,
    this.fabric,
    this.style,
    this.pattern,
    this.imageUrl,
    this.sku,
    this.description,
  });

  final String name;
  final String category;
  final String color;
  final List<String> sizes;
  final double price;
  final double cost;
  final int quantity;
  final String? colorHex;
  final String? fabric;
  final String? style;
  final String? pattern;
  final String? imageUrl;
  final String? sku;
  final String? description;

  Map<String, dynamic> toJson() {
    return {
      'itemName': name,
      'category': category,
      'color': color,
      'sizes': sizes,
      'price': price,
      'cost': cost,
      'quantity': quantity,
      if (colorHex != null && colorHex!.trim().isNotEmpty) 'colorHex': colorHex!.trim(),
      if (fabric != null && fabric!.trim().isNotEmpty) 'fabric': fabric!.trim(),
      if (style != null && style!.trim().isNotEmpty) 'style': style!.trim(),
      if (pattern != null && pattern!.trim().isNotEmpty) 'pattern': pattern!.trim(),
      if (imageUrl != null && imageUrl!.trim().isNotEmpty) 'imageUrl': imageUrl!.trim(),
      if (sku != null && sku!.trim().isNotEmpty) 'sku': sku!.trim(),
      if (description != null && description!.trim().isNotEmpty) 'description': description!.trim(),
    };
  }
}

/// Payload sent to `PUT /api/v1/orgs/{orgId}/catalog/items/{id}` to update an existing piece.
class UpdateProductPayload {
  const UpdateProductPayload({
    this.name,
    this.category,
    this.color,
    this.sizes,
    this.price,
    this.cost,
    this.quantity,
    this.colorHex,
    this.fabric,
    this.style,
    this.pattern,
    this.imageUrl,
    this.sku,
    this.description,
    this.status,
  });

  final String? name;
  final String? category;
  final String? color;
  final List<String>? sizes;
  final double? price;
  final double? cost;
  final int? quantity;
  final String? colorHex;
  final String? fabric;
  final String? style;
  final String? pattern;
  final String? imageUrl;
  final String? sku;
  final String? description;
  final String? status;

  Map<String, dynamic> toJson() {
    return {
      if (name != null) 'itemName': name,
      if (category != null) 'category': category,
      if (color != null) 'color': color,
      if (sizes != null) 'sizes': sizes,
      if (price != null) 'price': price,
      if (cost != null) 'cost': cost,
      if (quantity != null) 'quantity': quantity,
      if (colorHex != null) 'colorHex': colorHex,
      if (fabric != null) 'fabric': fabric,
      if (style != null) 'style': style,
      if (pattern != null) 'pattern': pattern,
      if (imageUrl != null) 'imageUrl': imageUrl,
      if (sku != null) 'sku': sku,
      if (description != null) 'description': description,
      if (status != null) 'status': status,
    };
  }
}
