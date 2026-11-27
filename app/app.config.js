// Adds Firebase (push through FCM) on top of app.json when the build has its config files.
// The files come from the Firebase console and stay out of git; on EAS they are file environment
// variables GOOGLE_SERVICES_JSON and GOOGLE_SERVICE_INFO_PLIST (steps in app/README.md, "Push notifications").
const fs = require('fs');
const path = require('path');

function file(envName, fallback) {
  const p = process.env[envName] || fallback;
  return fs.existsSync(path.resolve(__dirname, p)) ? p : null;
}

module.exports = ({ config }) => {
  const android = file('GOOGLE_SERVICES_JSON', './google-services.json');
  const ios = file('GOOGLE_SERVICE_INFO_PLIST', './GoogleService-Info.plist');
  if (!android || !ios) {
    if (process.env.EAS_BUILD_PROFILE === 'production') {
      throw new Error('Firebase config files are missing; a production build without push is not allowed.');
    }
    console.warn('[ailu] Firebase config missing: this build will have no push notifications.');
    return config;
  }
  return {
    ...config,
    ios: { ...config.ios, googleServicesFile: ios },
    android: { ...config.android, googleServicesFile: android },
    plugins: [
      ...config.plugins,
      '@react-native-firebase/app',
      '@react-native-firebase/messaging',
      // Firebase's iOS SDK ships through Swift Package Manager and needs dynamic frameworks.
      ['expo-build-properties', { ios: { useFrameworks: 'dynamic' } }],
    ],
  };
};
