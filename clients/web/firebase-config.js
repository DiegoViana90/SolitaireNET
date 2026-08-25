export const firebaseConfig = {
  apiKey: "COLE_AQUI_A_API_KEY",
  authDomain: "SEU_PROJETO.firebaseapp.com",
  projectId: "SEU_PROJETO",
  storageBucket: "SEU_PROJETO.firebasestorage.app",
  messagingSenderId: "COLE_AQUI_O_SENDER_ID",
  appId: "COLE_AQUI_O_APP_ID"
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
