open Thuja
open Thuja.Styles
open Thuja.Elements

let headers =
  [ "";  "Mon 03.08"; "Tue 04.08"; "Wed 05.08"; "Thu 06.08"; "Fri 07.08"; "Sat 08.08"; "Sun 09.08" ]

let rows = [ 
  for hour in [ 09..20 ] do
    for minute in [ 00; 30 ] do
      let mon = if 11 <= hour && hour <= 14 then "TASK-101" else ""
      let tue = if 10 <= hour && hour <= 15 then "TASK-120" else ""
      let wed = if 14 <= hour && hour <= 18 then "TASK-170" else ""
      let fri = if 15 <= hour && hour <= 17 then "TASK-201" else ""
      
      [ $"{hour:d2}:{minute:d2}"; mon; tue; wed; ""; fri; ""; "" ]
] 

let props =
  let layout = [ Absolute 5 ]@[ for _ in  1..7 do Fraction 1 ]
  let borderStyle = TableProps.BorderStyle Normal
  let textAlign = TableProps.TextAlign Center

  Seq.empty
  |> Seq.append [ Columns layout ]
  |> Seq.append [ borderStyle; textAlign ]
  |> Seq.toList

let view =
  table props headers rows

// program
Program.makeStatic view
|> Program.run