# Roadmap

1. Repeat the current verification suite on more Navi 10 boards and driver versions.
2. Generalize meshlet inputs and validate variable primitive counts, interpolated attributes, and output capacity handling against the direct reference.
3. Evaluate frame overlap, scratch-buffer reuse across submissions, and queues with GPU captures and explicit fence checks.
4. Define a reusable data and synchronization contract for integration with a graphics translator.
