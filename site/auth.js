import { firebaseConfig, isFirebaseConfigured } from "./firebase-config.js";
import { initializeApp } from "https://www.gstatic.com/firebasejs/10.12.5/firebase-app.js";
import {
  getAuth,
  GithubAuthProvider,
  GoogleAuthProvider,
  onAuthStateChanged,
  signInWithPopup,
  signOut
} from "https://www.gstatic.com/firebasejs/10.12.5/firebase-auth.js";

let auth = null;

if (isFirebaseConfigured()) {
  const app = initializeApp(firebaseConfig);
  auth = getAuth(app);
}

export function authConfigured() {
  return Boolean(auth);
}

export function subscribeAuth(callback) {
  if (!auth) {
    callback(null);
    return () => {};
  }

  return onAuthStateChanged(auth, callback);
}

export async function signInWithGoogle() {
  if (!auth) throw new Error("Firebase ainda nao foi configurado.");
  const provider = new GoogleAuthProvider();
  provider.setCustomParameters({ prompt: "select_account" });
  return signInWithPopup(auth, provider);
}

export async function signInWithGitHub() {
  if (!auth) throw new Error("Firebase ainda nao foi configurado.");
  const provider = new GithubAuthProvider();
  return signInWithPopup(auth, provider);
}

export async function signOutUser() {
  if (!auth) return;
  await signOut(auth);
}

export async function getCurrentUserToken() {
  if (!auth?.currentUser) return null;
  return auth.currentUser.getIdToken();
}
