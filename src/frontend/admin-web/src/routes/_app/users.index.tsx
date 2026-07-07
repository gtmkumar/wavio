import { createFileRoute } from "@tanstack/react-router";

// The directory lives in the users.tsx layout route; with no sheet open there
// is nothing extra to render at /users itself.
export const Route = createFileRoute("/_app/users/")({
  component: () => null,
});
