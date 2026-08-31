# PROJECT MIRROR Demo UI Design QA

## Comparison target

- Source visual truth:
  - `C:\Users\yuewi\AppData\Local\Temp\codex-clipboard-c9ee2465-aea2-4437-a2fd-5417d4e67a9d.png`
  - `C:\Users\yuewi\AppData\Local\Temp\codex-clipboard-7b3ef71e-f1f4-4b46-af1f-eece593e9297.png`
- Rendered implementation:
  - `_qa/formal-demo/02b-tutor-dialogue.png`
  - `_qa/formal-demo/02c-background-03.png`
  - `_qa/formal-demo/03-bash-gameplay.png`
- Side-by-side evidence:
  - `_qa/formal-demo/comparison-dialogue.png`
  - `_qa/formal-demo/comparison-gameplay.png`
- Viewport: 1920 x 1080 CSS/render pixels at density 1.
- Source pixels: 1832 x 1027 for dialogue and 1893 x 1050 for gameplay.
- Implementation pixels: 1920 x 1080 for both states.
- Normalization: each source and implementation capture was resized to 960 x 540 and placed side by side without cropping.
- States: first Tutor introduction line, S-17 response line, and first playable Bash turn before selection.

## Full-view comparison evidence

The dialogue screen preserves the reference hierarchy: header, centred identity portrait, speaker status, and a large dialogue region. The gameplay screen preserves the reference's left status column, central lattice and action row, circular confirmation control, bottom-left Tutor identity, bottom dialogue region, top-right SYSTEM region, and bottom-right Save & Back action.

The reference's white masks and dark text are intentionally not reproduced because the user's explicit implementation direction supersedes those details: panel interiors are fully transparent and original dark text is fluorescent green.

## Focused-region comparison evidence

- `_qa/formal-demo/02c-background-03.png` confirms that an authored `S-17` line switches both the speaker label and portrait to SUBJECT S-17.
- `_qa/formal-demo/03-bash-gameplay.png` confirms the fixed 470 px SYSTEM frame and the requested three-column gameplay composition.
- `FormalDemoSmoke.gd` injects 80 overflow lines and verifies that the SYSTEM vertical scrollbar becomes visible with a scroll range larger than one page.

## Required fidelity surfaces

- Fonts and typography: hierarchy follows the reference and all former dark foreground text is mapped to fluorescent green with a restrained dark outline for readability. Tutor red and S-17 cyan remain semantic identity accents.
- Spacing and layout rhythm: major regions match the reference ordering and proportions. The Tutor and S-17 identity cards use the same dimensions so switching speakers does not move the dialogue region.
- Colors and visual tokens: white translucent panel fills and their fill-producing shadows were removed. Green borders, semantic choice colors, the blue circular confirm control, Tutor red, and S-17 cyan remain intentional.
- Image quality and asset fidelity: supplied placeholder initials were replaced with two transparent, project-local raster portraits at native 1254 x 1254 resolution. Godot scales them with preserved aspect ratio.
- Copy and content: existing English game copy is retained. One of the six introduction pages is now an S-17 response while the Tutor's identity and evaluation explanation remain present.

## Comparison history

### Pass 1

- P1: transparent panels still appeared green because StyleBox shadows rendered beneath their interiors. Fixed by removing panel and portrait-frame shadows while retaining borders.
- P2: the disabled confirm control used the global rectangular disabled style. Fixed by assigning the circular confirm style to the disabled state.
- P2: the fourth Round Status item was clipped. Fixed by widening the left column and tightening its body typography.

### Pass 2

- Post-fix captures show transparent interiors, a circular disabled confirm control, complete Round Status copy, and no overlapping or clipped persistent controls.
- No actionable P0, P1, or P2 visual differences remain after accounting for the user's explicit transparent/neon override.

## Interaction and runtime checks

- Primary interactions tested: New Game, click-to-complete typewriter, click-to-advance dialogue, Tutor/S-17 identity switch, choice selection, confirm, Limit Bash reveal, SYSTEM history, SYSTEM overflow scrolling, Save & Back, Continue, overwrite guard, and completed summary return.
- Console errors checked in headless and OpenGL Compatibility runs: none.
- Domain test result: 459 assertions passed.

## Follow-up polish

- P3: the generated portraits are more detailed than the reference placeholders. This is acceptable for the requested functional character display and can be art-directed further later without changing layout or dialogue logic.

final result: passed
