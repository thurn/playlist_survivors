import { expect, test } from "@playwright/test";

test("page loads", async ({ page }) => {
  await page.goto("http://localhost:5199/");
  await expect(page.locator("#app")).toBeAttached();
});
