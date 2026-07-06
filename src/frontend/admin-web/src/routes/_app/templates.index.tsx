import { createFileRoute } from "@tanstack/react-router";

// The list lives in the templates.tsx layout route; with no sheet open there
// is nothing extra to render at /templates itself.
export const Route = createFileRoute("/_app/templates/")({
  component: () => null,
});
