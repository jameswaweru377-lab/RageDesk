import React from "react";
import { createRoot } from "react-dom/client";
import App from "./App";
import "./styles.css";
import "./visual-parity.css";
import "./deep-navy-finish.css";

const root = document.getElementById("root");
if (!root) throw new Error("RageDesk frontend root missing");

createRoot(root).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>
);
