import { readParams, runApp } from "./runtime/app.ts";

void runApp(document.getElementById("app")!, readParams(location.search));
