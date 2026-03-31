import {
  createStartHandler,
  defaultStreamHandler,
} from "@tanstack/react-start/server";

const handler = createStartHandler({
  handler: defaultStreamHandler,
});

export default {
  fetch: handler,
};
