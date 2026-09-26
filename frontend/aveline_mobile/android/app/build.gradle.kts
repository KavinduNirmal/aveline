import java.io.FileInputStream
import java.util.Properties

plugins {
    id("com.android.application")
    // The Flutter Gradle Plugin must be applied after the Android and Kotlin Gradle plugins.
    id("dev.flutter.flutter-gradle-plugin")
}

// Generate the Firebase string resources (google_app_id, gcm_defaultSenderId, ...) only when a
// config is present. google-services.json is gitignored and supplied from a CI secret, so a
// checkout without it must still build: fork pull requests never receive secrets, and a local
// release build should not require Firebase credentials. The app then starts without FCM, which
// the NoopPushTokenSource fallback already handles.
//
// NOTE: the package_name inside that file must match `applicationId` below. The Google Services
// plugin fails the build with "No matching client found for package name" when they disagree, so
// changing the id means registering the new one in the Firebase project first.
if (file("google-services.json").exists()) {
    apply(plugin = "com.google.gms.google-services")
}

// ---------------------------------------------------------------------------------------------
// Release signing.
//
// `key.properties` and the keystore it points at are both gitignored: the release workflow
// materialises them from repository secrets, and neither a local checkout nor a fork PR has them.
// So the release signing config is created only when the file is actually present, and the release
// build type falls back to the debug key otherwise - the Flutter template's behaviour, which keeps
// `flutter run --release` working locally without any signing setup.
//
// That fallback is exactly why release-apk.yml asserts the certificate before publishing: an APK
// that quietly fell back is debug-signed, and the debug key is public and password-free.
// ---------------------------------------------------------------------------------------------
val keystorePropertiesFile = rootProject.file("key.properties")
val hasReleaseSigning = keystorePropertiesFile.exists()
val keystoreProperties = Properties().apply {
    if (hasReleaseSigning) {
        FileInputStream(keystorePropertiesFile).use { load(it) }
    }
}

android {
    namespace = "dev.gravora.aveline"
    compileSdk = flutter.compileSdkVersion
    ndkVersion = flutter.ndkVersion

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    defaultConfig {
        applicationId = "dev.gravora.aveline"
        // You can update the following values to match your application needs.
        // For more information, see: https://flutter.dev/to/review-gradle-config.
        minSdk = flutter.minSdkVersion
        targetSdk = flutter.targetSdkVersion
        // Uses the version code from pubspec.yaml. When using split APKs, 1000 * ABI_VERSION
        // is added automatically by Flutter. (https://developer.android.com/studio/build/configure-apk-splits#configure-APK-versions)
        // You can force using the value of versionCode by specifying the `-P force-version-code-ignoring-abi=true`
        // flag during build.
        versionCode = flutter.versionCode
        versionName = flutter.versionName
    }

    signingConfigs {
        if (hasReleaseSigning) {
            create("release") {
                keyAlias = keystoreProperties.getProperty("keyAlias")
                keyPassword = keystoreProperties.getProperty("keyPassword")
                storeFile = file(keystoreProperties.getProperty("storeFile"))
                storePassword = keystoreProperties.getProperty("storePassword")
            }
        }
    }

    buildTypes {
        release {
            signingConfig = if (hasReleaseSigning) {
                signingConfigs.getByName("release")
            } else {
                signingConfigs.getByName("debug")
            }
        }
    }
}

kotlin {
    compilerOptions {
        jvmTarget = org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17
    }
}

flutter {
    source = "../.."
}
