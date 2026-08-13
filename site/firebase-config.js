export const firebaseConfig = {
  apiKey: "REDACTED_FIREBASE_API_KEY",
  authDomain: "paciencianet.firebaseapp.com",
  projectId: "paciencianet",
  storageBucket: "paciencianet.firebasestorage.app",
  messagingSenderId: "917756305847",
  appId: "1:917756305847:web:9b859aad9a338888b08832"
};

export function isFirebaseConfigured() {
  return Boolean(
    firebaseConfig.apiKey &&
    firebaseConfig.appId &&
    !firebaseConfig.apiKey.startsWith("COLE_AQUI") &&
    !firebaseConfig.appId.startsWith("COLE_AQUI") &&
    firebaseConfig.authDomain !== "SEU_PROJETO.firebaseapp.com" &&
    firebaseConfig.projectId !== "SEU_PROJETO");
}
