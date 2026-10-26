/** The portal's button words for a task: "Schedule your fingerprinting" → "Click here to schedule your fingerprinting". */
export const callToAction = (taskLabel: string) =>
  taskLabel
    ? `Click here to ${taskLabel.charAt(0).toLowerCase()}${taskLabel.slice(1)}`
    : 'Click here to start';
