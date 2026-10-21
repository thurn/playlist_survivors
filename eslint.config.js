import js from "@eslint/js";
import tseslint from "typescript-eslint";
import globals from "globals";

export default tseslint.config(
  { ignores: ["dist/", "node_modules/", "tools/analysis/", "captures/", "public/", "test-results/", "playwright-report/"] },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  {
    languageOptions: { globals: { ...globals.browser, ...globals.node } },
    rules: {
      "@typescript-eslint/no-unused-vars": ["error", { argsIgnorePattern: "^_", varsIgnorePattern: "^_" }],
      "@typescript-eslint/no-non-null-assertion": "off",
    },
  },
  {
    files: ["src/planner/**/*.ts", "src/core/**/*.ts", "src/mapgen/**/*.ts"],
    rules: {
      "no-restricted-properties": ["error",
        { object: "Math", property: "random", message: "Planner code must be deterministic; use the seeded RNG." },
        { object: "Date", property: "now", message: "Planner code must be deterministic." }],
      "no-restricted-syntax": ["error", { selector: "NewExpression[callee.name='Date']", message: "Planner code must be deterministic." }],
    },
  },
);
