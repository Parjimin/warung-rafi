# Counter / Receipt design system

Reference: index.html. Open directly; no npm/server/account required. Test controls in the footer simulate states and never connect to real services.

## Tokens
| Role | Value | Native resource |
|---|---|---|
| Background | #F3F0E9 | AppBackground |
| Surface | #FEFEFE | SurfaceBrush |
| Elevated / settlement | #FAF8F3 | ElevatedBrush |
| Text | #282722 | Ink |
| Secondary | #817E72 | Muted |
| Border | #E5E0D5 | Line |
| Accent | #FCB711 | Forest |
| Rail | #292A24 | RailBrush |
| Selected | #FFF7DF | SelectedBrush |
| Success | #28644A | SuccessBrush |
| Warning | #986326 | WarningBrush |
| Danger | #AC433C | DangerBrush |

Segoe UI: title 28 semibold, section 22 semibold, body 15–16, label 13, helper 11–12, eyebrow 10, card price 16–18 semibold, total 30 semibold. No remotely loaded fonts.
Spacing: 4 / 8 / 12 / 16 / 20 / 24 / 32. Radius: inputs 8, tiles/buttons 10, primary panels 14. Borders 1 DIP, selected 2 DIP. Shadow only on major overlay if needed; ordinary WPF cards have no effects.

## Layout mapping
- Shell: horizontal top navigation → WPF Grid rows 76,34,*,36.
- Catalog: header, categories, scrollable four-column tiles. Native WrapPanel changes widths on viewport change, not on quantity updates.
- 1280: four columns, cards >=150 wide and 56–88 high, sized to viewport. 900: three columns, >=125 wide; cart 300–360 wide. 1536+ capped four columns for consistent scanning.
- Order slip: Grid Auto,*,Auto; total and actions pinned. Stepper has 44x44 targets. Item content scrolls independently.
- Payment: order summary on left, amount / keypad / method on right, footer pinned. In HTML shown as dialog; native full workspace payment stage retains same hierarchy and more vertical room at 900x620.
- History: full workspace records with fixed action column; filter persists within view.
- Settings: existing native owner-protected window, shared tokens and control templates. No retired cash/backup/restore entries.

## Component mapping
| HTML | WPF |
|---|---|
| primary/secondary button | shared Button template + PrimaryButton / SecondaryButton |
| rail item | NavigationButton and native geometry icon |
| product tile | ProductCard style, Grid Auto,* and quantity badge |
| category tab | CategoryButton + selected brush |
| quantity stepper | StepperButton and selected-neutral Border |
| search/input | TextBox template + InputWithHint |
| order slip | OrderSlip Border, pinned grid rows |
| status | compact footer, existing status TextBlocks |
| toast | noninteractive native overlay; QRIS event logic unchanged |
| dialog | existing native window / MessageBox confirmation, shared typography |
| payment option | PaymentOption style + selection |
| history row | flat Surface with line border, compact type |

## States / motion
Hover changes surface/border; pressed response immediate. Selected product uses border, pale fill, numeric badge. Disabled remains visible with reduced opacity. Focus ring supports keyboard. 130ms opacity transitions for navigation and 180ms translate-X for bottom-left QRIS toast, no layout animation, no custom scrolling. Reduced motion follows SystemParameters.ClientAreaAnimation / prefers-reduced-motion.

Empty states describe next action. Errors never imply transaction loss. Offline sales remain local-first. QRIS notification is informational and never auto-completes a sale. Printer failure keeps the saved sale and offers reprint through existing success/history controls.

## Validation scope
Browser: real DOM interactions, 32 products, four columns at 1280, resize 900, add/quantity/total, cash validation/change/completion, history, state switching, JavaScript errors. Native: existing transaction, storage recovery, sync, payment, refund, owner settings checks plus resized WPF layouts and screenshot comparison. Physical printer, live payment, latency on cashier laptop remain device checks.

## Approved Asoyyy direction
User approved the yellow V2 prototype on 4 October 2026. Native normal mode has no prototype labels; isolated payment demo retains its explicit simulation notice. Original icon is bundled in the header, full logo is rendered black with its original alpha for thermal receipts, Instagram @asoyyy_group.
