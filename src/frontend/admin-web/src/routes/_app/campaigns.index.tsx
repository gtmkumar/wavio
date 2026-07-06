import { createFileRoute } from "@tanstack/react-router";

// The list lives in the campaigns.tsx layout route; with no sheet open there
// is nothing extra to render at /campaigns itself.
export const Route = createFileRoute("/_app/campaigns/")({
  component: () => null,
});
