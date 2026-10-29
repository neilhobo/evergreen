import React from "react";
import ReactDOM from "react-dom/client";
import App from "./App";

// Bundle loaded fine — reset the index.html boot watchdog's retry counter.
sessionStorage.removeItem("bootRetry");

ReactDOM.createRoot(document.getElementById("root") as HTMLElement).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>,
);
