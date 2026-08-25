import { firebaseConfig, isFirebaseConfigured } from "./firebase-config.js";

let auth = null;
let firebaseModules = null;
let firebaseLoad = null;
let authReady = null;

async function loadFirebase() {
  if (!isFirebaseConfigured()) return null;

  if (!firebaseLoad) {
    firebaseLoad = Promise.all([
      import("https://www.gstatic.com/firebasejs/10.12.5/firebase-app.js"),
      import("https://www.gstatic.com/firebasejs/10.12.5/firebase-auth.js")
    ]).then(([appModule, authModule]) => {
      const app = appModule.initializeApp(firebaseConfig);
      auth = authModule.getAuth(app);
      firebaseModules = authModule;
      return firebaseModules;
    });
  }

  return firebaseLoad;
}

export function authConfigured() {
  return isFirebaseConfigured();
}

export function currentUser() {
  return auth?.currentUser || null;
}

export async function waitForAuthReady() {
  const modules = await loadFirebase();
  if (!auth || !modules) return null;

  if (!authReady) {
    authReady = new Promise((resolve) => {
      const unsubscribe = modules.onAuthStateChanged(
        auth,
        (user) => {
          unsubscribe();
          resolve(user);
        },
        () => resolve(null));
    });
  }

  return authReady;
}

export function subscribeAuth(callback) {
  if (!isFirebaseConfigured()) {
    callback(null);
    return () => {};
  }

  let unsubscribe = null;
  let active = true;

  loadFirebase()
    .then((modules) => {
      if (!active || !modules) return;
      unsubscribe = modules.onAuthStateChanged(auth, callback);
    })
    .catch(() => callback(null));

  return () => {
    active = false;
    unsubscribe?.();
  };
}

export async function signInWithGoogle() {
  const modules = await loadFirebase();
  if (!auth || !modules) throw new Error("Firebase ainda nao foi configurado.");
  const provider = new modules.GoogleAuthProvider();
  provider.setCustomParameters({ prompt: "select_account" });
  return modules.signInWithPopup(auth, provider);
}

export async function signOutUser() {
  const modules = await loadFirebase();
  if (!auth || !modules) return;
  await modules.signOut(auth);
}

export async function getCurrentUserToken() {
  await waitForAuthReady();
  const user = currentUser();
  if (!user) return null;
  return user.getIdToken();
}
