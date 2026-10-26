import { createFileRoute } from '@tanstack/react-router';

export const Route = createFileRoute('/(authenticated)/admin/staff')({
  component: () => <h1 className="text-3xl font-bold">Staff</h1>,
});
