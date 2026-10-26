import { createFileRoute, redirect } from '@tanstack/react-router';

export const Route = createFileRoute('/(authenticated)/admin/')({
  beforeLoad: () => {
    throw redirect({ to: '/admin/templates' });
  },
});
