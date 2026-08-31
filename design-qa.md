# PROJECT MIRROR Demo UI Design QA

## Comparison target

- Source visual truth:
  - `C:\Users\yuewi\AppData\Local\Temp\codex-clipboard-c9ee2465-aea2-4437-a2fd-5417d4e67a9d.png`
  - `C:\Users\yuewi\AppData\Local\Temp\codex-clipboard-7b3ef71e-f1f4-4b46-af1f-eece593e9297.png`
  - `C:\Users\yuewi\AppData\Local\Temp\codex-clipboard-26957a17-2300-4b1b-b3d5-0caf76276804.png`
  - `C:\Users\yuewi\AppData\Local\Temp\codex-clipboard-611f6f2f-bb7c-4e88-9fa6-1e104056bebd.png`
  - `_qa/formal-demo/approved-effect-mockup.png`
- Rendered implementation:
  - `_qa/formal-demo/02c-background-01.png`
  - `_qa/formal-demo/02c-background-03.png`
  - `_qa/formal-demo/03-bash-gameplay.png`
  - `_qa/formal-demo/03-hover-choice.png`
  - `_qa/formal-demo/03a-confirm-hover.png`
  - `_qa/formal-demo/03b-tutor-locked.png`
  - `_qa/formal-demo/03c-bash-result.png`
  - `_qa/formal-demo/03d-limit-tutor-acting.png`
- Side-by-side evidence:
  - `_qa/formal-demo/comparison-dialogue-current.png`
  - `_qa/formal-demo/comparison-gameplay-current.png`
  - `_qa/formal-demo/comparison-text-effect.png`
  - `_qa/formal-demo/comparison-approved-effect.png`
  - `_qa/formal-demo/comparison-disabled-confirm-pass7.png`
  - `_qa/formal-demo/comparison-disabled-icon-pass8.png`
- Source pixels: 1832 x 1027 for dialogue, 1893 x 1050 for gameplay,
  and 978 x 722 for the phosphor-text reference.
- Implementation pixels: 1920 x 1080 at density 1.
- Pass 7 disabled-state source pixels: 210 x 210. The 1920 x 1080
  implementation was cropped to a 240 x 240 Confirm region, then normalized to
  210 x 210 and placed beside the source in one 420 x 210 comparison image.
- Pass 8 reuses that 210 x 210 source crop and compares it with the final
  pre-rendered icon implementation at the same scale.
- Layout comparisons fit each full source and implementation capture into one
  960 x 540 half without cropping. The text-effect comparison uses focused
  source and dialogue-panel crops in a single 1920 x 520 image.

## Full-view comparison evidence

The dialogue screen retains the reference hierarchy while shifting more of the
available height to the identity card: the portrait frame is now 380 x 380,
the portrait card grows to 720 x 520 minimum, and the dialogue card contracts
from 330 px to 250 px. Tutor and S-17 still use identical geometry, so speaker
switches do not move the dialogue region.

The gameplay screen now places the active quantity to the left of the lattice
and the staged selection to the right. The 390 px SYSTEM frame shares the exact
top and bottom line of the 390 px central lattice frame. The bottom Tutor card
and dialogue frame both use a 362 px height and share the same y position.

The reference's white masks and dark text remain intentionally superseded by
the user's explicit implementation direction: panel interiors are transparent
and former dark foreground text is fluorescent green.

## Focused-region comparison evidence

- `comparison-text-effect.png` verifies the requested phosphor treatment in one
  view. The implementation uses a 3 px dot pitch, horizontal scanline breakup,
  brightness lift, and a restrained four-direction halo while retaining small
  text legibility.
- `comparison-approved-effect.png` places the confirmed effect mockup beside the
  final Godot render. It verifies the sharp science-fiction chamber, saturated
  phosphor green palette, centered choice row, semantic button text colors, and
  enlarged centered Tutor dialogue in one comparison surface.
- `02c-background-03.png` confirms that an authored S-17 line switches both the
  speaker label and the enlarged portrait to SUBJECT S-17.
- `03-bash-gameplay.png` confirms the left / lattice / right quantity placement
  and the top-row frame alignment.
- `03-hover-choice.png` confirms a brighter fill, thicker border, and yellow
  halo on a hovered choice. `03a-confirm-hover.png` confirms the enabled fourth
  action remains available after a staged choice; automated style checks cover
  all three choices and Confirm.
- The smoke test injects 80 overflow lines and verifies that the SYSTEM vertical
  scrollbar becomes visible with a range larger than one page.
- `comparison-disabled-confirm-pass7.png` places the user's problematic masked
  Confirm state beside the first transparent correction. The final
  `comparison-disabled-icon-pass8.png` verifies the thinner pre-rendered X,
  hidden underlying caption, transparent interior, and precise intersection
  with the circular frame. `03b-tutor-locked.png` verifies corner-to-corner
  alignment on all three rectangular choices.

## Required fidelity surfaces

- Fonts and typography: Label and RichTextLabel rendering receives the shared
  dot-matrix shader. It operates only on glyph canvas items, so portrait images,
  transparent panel interiors, and borders remain unaffected. Buttons retain
  the fluorescent theme color and use dedicated interaction styling. Tutor
  dialogue is 30 px during gameplay and 32 px in dialogue-only scenes, with
  centred paragraph and vertical alignment.
- Spacing and layout rhythm: the portrait-led dialogue ratio, lateral quantity
  placement, top-row SYSTEM alignment, and bottom Tutor/dialogue alignment now
  match the supplied compositions.
- Colors and visual tokens: transparent panel fills, #39FF3A-style fluorescent
  green text and borders, semantic choice colors, transparent disabled controls,
  the blue enabled Confirm control, Tutor red, and S-17 cyan remain intentional.
- Image quality and asset fidelity: Tutor and S-17 use the existing project-local
  1254 x 1254 portraits with preserved aspect ratio. The new project-local
  1920 x 1080 command-chamber background is sharp and free of UI/text artefacts.
  Disabled controls use the project-local 512 x 512 transparent
  `assets/ui/dot_matrix_x.png` bitmap with nearest-neighbour filtering and
  runtime semantic tinting.
- Copy and content: existing game copy, dialogue flow, S-17 identity switching,
  and the System execution history are unchanged.

## Comparison history

### Pass 1

- P1: transparent panels still appeared green because StyleBox shadows rendered
  beneath their interiors. Fixed by removing panel shadows while retaining borders.
- P2: the disabled Confirm control used the global rectangular disabled style.
  Fixed by assigning the circular Confirm style to the disabled state.
- P2: the fourth Round Status item was clipped. Fixed by widening the left column
  and tightening its body typography.

### Pass 2

- Post-fix captures showed transparent interiors, a circular disabled Confirm
  control, complete Round Status copy, and no clipped persistent controls.

### Pass 3

- P1: SYSTEM extended 80 px below the central frame. Fixed by setting both frames
  to a 390 px reference height.
- P1: active and staged quantities were stacked above and below the lattice.
  Fixed with a three-part state row: active count, lattice, staged selection.
- P1: the Tutor card and bottom dialogue had mismatched heights and y positions.
  Fixed with matched 362 px frames and a deterministic 150 px left-column spacer.
- P2: the three choices and Confirm reused their normal hover styles. Fixed with
  four dedicated brighter, thicker, halo-backed hover states.

### Pass 4

- Real OpenGL Compatibility captures at 1920 x 1080 show the intended dot-matrix
  breakup without unreadable counters or body copy.
- Side-by-side montage review found no overlapping, clipped, off-canvas, or
  misaligned persistent controls. No actionable P0, P1, or P2 visual differences
  remain after accounting for the explicit transparent/neon override.

### Pass 5

- P1: the bright laboratory background washed the green typography toward mint.
  Fixed by replacing it with the approved sharp dark command chamber and changing
  the active phosphor palette toward #39FF3A.
- P1: the three rectangular choices sat at the top of their 150 px action region.
  Fixed by centering the ChoiceStack; all four action centres now share one y value.
- P1: all three choice labels inherited green. Fixed with green, yellow, and red
  state-specific font colors matching each corresponding frame.
- P2: Tutor body copy was 21 px and top aligned in a 362 px frame. Fixed with
  30 px centred gameplay dialogue and 32 px centred dialogue-only copy.
- Final side-by-side review found no actionable P0, P1, or P2 differences from
  the user-confirmed visual direction.

### Pass 6

- P1: native Button captions bypassed the glyph shader. Fixed with independent
  caption layers that preserve each normal, hover, pressed, and disabled color
  while applying the same phosphor dot-matrix material used by other text.
- P1: Title, CURRENT STATUS, and SYSTEM divider lines remained solid. Fixed
  with a screen-space dot-matrix line shader; the chapter divider shares it.
- P1: choices still used 1/2/3 and enlarged after selection. Fixed with stable
  A/B/C headings, unchanged button geometry, and removal of the selected tick.
- P1: Tutor turns left a selected choice visually interactive. Fixed by locking
  every choice for the full Tutor turn and adding a large shader-driven X plus
  darker semantic disabled styles.
- P1: result screens depended on a visible Continue button and retained the old
  result background. Fixed with pulsing enlarged outcome text and click-anywhere
  skip; removing the button also restores Tutor/dialogue frame alignment.
- P1: result and SUMMARY phases referenced the retired bright/result artwork.
  Both now use the sharp command chamber, and all four old PNG/import files were
  removed after a repository-wide reference check.
- Final OpenGL captures confirm readable dot-matrix button captions, visibly
  locked Tutor controls, aligned result panels, enlarged result animation text,
  and the command-chamber SUMMARY background.

### Pass 7

- P1: disabled controls still retained tinted background masks. Fixed by using
  fully transparent disabled StyleBoxes for all three choices and Confirm while
  preserving their semantic fluorescent borders.
- P1: the X shared the caption's padded layer, remained too small, and overlapped
  underlying text. Fixed by making it a button-level shader layer, hiding the
  caption while disabled, stretching the X to the clipped frame bounds, and
  retaining the dot-matrix material.
- P1: Limit Reveal hid Confirm and allowed the action row to reflow. Fixed by
  keeping Confirm visible and disabled with `TUTOR / ACTING`; the test records
  all four pre-action positions and confirms exact equality during reveal.
- P1: PLAYER WIN/LOSE occupied only the active-count slot. Fixed with an
  independent 56%-viewport result layer, 184 px pulsing display text, gold
  `#FFD21F` victory, and vivid red `#FF0038` defeat.
- Post-fix D3D12 captures at 1920 x 1080 show no mask, clipping, caption overlap,
  action-row movement, or weak result hierarchy. The focused 420 x 210
  side-by-side comparison contains no actionable P0/P1/P2 mismatch with the
  user's revised disabled-state direction.

### Pass 8

- P1: the stretched font glyph produced strokes that were too thick, covered
  too much of the hidden-caption region, and did not place all four tips at the
  frame corners. Fixed by replacing the glyph layer with one pre-rendered,
  transparent, 512 x 512 dot-matrix X texture.
- Rectangular choices place the texture 3 px inside the frame, making every tip
  meet an inner corner. Circular Confirm uses a 22 px inset, so the diagonals
  meet the circle at its four 45-degree intersections. The texture is tinted at
  runtime to retain each control's green, yellow, or red semantic color.
- D3D12 captures `03b-tutor-locked.png` and
  `03d-limit-tutor-acting.png` confirm thin strokes, no caption overlap, fully
  transparent disabled interiors, and invariant action-row geometry. The
  focused `comparison-disabled-icon-pass8.png` contains no actionable
  P0/P1/P2 mismatch with the revised direction.

## Interaction and runtime checks

- Primary interactions tested: New Game, click-to-complete typewriter,
  click-to-advance dialogue, Tutor/S-17 identity switch, choice hover and
  selection, Confirm, Limit Bash reveal, SYSTEM history, SYSTEM overflow
  scrolling, Save & Back, Continue, overwrite guard, and completed summary return.
- Geometry checks: lateral active/selection ordering, 390 px top-row alignment,
  matched Tutor/dialogue position and size, shared action-button centre line,
  semantic choice text colors, stable A/B/C button geometry, aligned result
  Tutor/dialogue frames, and distinct normal/hover/disabled StyleBoxes.
- Result-flow checks: no gameplay Continue node, enlarged pulsing result text,
  click-anywhere advance, and sharp command-chamber result/SUMMARY textures.
- Disabled-state checks: zero-alpha fills, the imported 512 x 512 dot-matrix X
  texture, nearest-neighbour filtering, correct rectangular/circular overlay
  geometry, and invariant positions across Bash Tutor and Limit Reveal states.
- Console errors checked in headless and D3D12 Forward Mobile runs: none.
- Domain test result: 459 assertions passed.

## Follow-up polish

- P3: the project portraits are more detailed than the original placeholder
  circles. This is retained because it supports the functional Tutor/S-17
  identity requirement without changing the requested layout.

final result: passed
