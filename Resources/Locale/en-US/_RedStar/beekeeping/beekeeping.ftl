guide-entry-beekeeping = Beekeeping
guide-entry-beekeeping-analyzer = Apiary analyzer
guide-entry-beekeeping-queen = Queen and traits
## Hive

beekeeping-slot-queen = Queen
beekeeping-slot-frame-1 = Frame 1
beekeeping-slot-frame-2 = Frame 2
beekeeping-slot-frame-3 = Frame 3
beekeeping-slot-frame-4 = Frame 4

beekeeping-hive-examine-queen = The hive has a [color=yellow]queen[/color].
beekeeping-hive-examine-no-queen = The hive has [color=red]no queen[/color].
beekeeping-hive-examine-frames = Frames: { $count } of { $max }.
beekeeping-hive-examine-bees = Bees in the hive: { $bees } of { $max }{ $outside ->
    [0] {""}
   *[other] , { $outside } more outside
}.
beekeeping-hive-examine-nectar = Nectar: { $level ->
    [Empty] none
    [Low] a little
    [Medium] some
   *[High] plenty
}.
beekeeping-hive-examine-cramped = [color=yellow]The hive is cramped: the bees need more frames.[/color]
beekeeping-hive-examine-leaving = [color=red]Bees are leaving the hive: not enough frames.[/color]
beekeeping-hive-examine-dying = [color=red]Without a queen the colony is dying out.[/color]
beekeeping-hive-examine-scout-outside = The scout is out of the hive.
beekeeping-hive-examine-scout-lost = The hive lost its scout. A new one in { $minutes } min { $seconds } s.
beekeeping-hive-examine-scout-lost-grow = The hive lost its scout. A new one comes once the colony grows.
beekeeping-hive-examine-paused = [color=orange]The bees have gone quiet: { $reason ->
    [Vacuum] there is no air to breathe
    [HighPressure] the pressure is too high
    [Cold] it is too cold
    [Heat] it is too hot
   *[Fire] there is a fire
}.[/color]

beekeeping-verb-category-frames = Take out
beekeeping-verb-category-bees = Catch

## Frames

beekeeping-frame-examine = { $state ->
    [None] Empty foundation
    [Honey] The combs are filling with honey
   *[Wax] The combs are capped, time to cut them
}. Combs: { $combs } of { $max }.
beekeeping-frame-cut-empty = There is nothing to cut off this frame.
beekeeping-frame-cut-caps = You cut off the wax caps: wax ({ $wax }). Now the combs can be cut.
beekeeping-frame-cut-combs = You cut off the combs ({ $combs }).
beekeeping-frame-pry-in-hand = Put the frame on the floor to take it apart.

## Bees in hand

beekeeping-take-too-many = Too many bees of this hive are outside already.
beekeeping-forager-caught = You catch the lost bee. Take it back to its hive.
beekeeping-take-scout-flying = The scout is flying now; wait until she is back.
beekeeping-take-no-scout = There is no scout in the hive.
beekeeping-take-no-workers = There are no free worker bees in the hive.
beekeeping-take-scout-done = You carefully take the scout out.
beekeeping-take-pollinator-done = You carefully take a worker bee out.
beekeeping-return-done = The bee goes back into the hive.
beekeeping-return-wrong-hive = This is not the bee's hive.
beekeeping-return-crowded = The hive is too crowded: the bee cannot go back until the frames are returned.

beekeeping-bee-examine-scout = A scout. Trays remembered: { $count } of { $max }.
beekeeping-bee-examine-pollinator = A pollinator. { $charges ->
    [0] No pollen left.
    [one] Pollen for { $charges } pollination.
   *[other] Pollen for { $charges } pollinations.
}
beekeeping-bee-examine-dying = [color=red]The bee will die soon.[/color]

beekeeping-scout-no-grid = The scout will not remember a tray that is not on the floor of a ship or station.
beekeeping-scout-known = The scout already knows this tray.
beekeeping-scout-remembered = The scout remembers the tray ({ $count } of { $max }).

beekeeping-pollinate-no-charges = The bee has no pollen left.
beekeeping-pollinate-no-plant = There is nothing to pollinate here.
beekeeping-pollinate-dead = The plant is dead.
beekeeping-pollinate-self-harvest = This plant harvests itself; pollinating it is useless.
beekeeping-pollinate-ripe = The plant is ripe already. Harvest it first.
beekeeping-pollinate-already = The plant is already pollinated until the next harvest.
beekeeping-pollinate-bonus = The bee pollinates the plant. The next harvest will be doubled!
beekeeping-pollinate-no-bonus = The bee pollinates the plant, but the harvest will not be doubled.
beekeeping-plant-examine-pollinated = The plant is [color=yellow]pollinated[/color] until the next harvest.
beekeeping-plant-examine-pollinated-bonus = The plant is [color=yellow]pollinated[/color]: the next harvest will be doubled.

## Traits

bee-trait-pest-control = Pest control
bee-trait-pest-control-desc = Pollination lowers pests and weeds in the tray. Innate, takes no slot.
bee-trait-sting = Sting
bee-trait-sting-desc = Reagents injected into the queen are carried in the bees' stings and slowly go into the combs.
bee-trait-aggressive = Aggression
bee-trait-aggressive-desc = The bees are angry.
bee-trait-productive = Productive
bee-trait-productive-desc = Combs are built faster, but each costs more nectar.
bee-trait-efficient-foraging = Efficient foraging
bee-trait-efficient-foraging-desc = Each visit brings more nectar.
bee-trait-improved-pollination = Improved pollination
bee-trait-improved-pollination-desc = Pollination doubles the harvest more often, but a pollinator has one charge less.

bee-trait-keen-scout = Keen scout
bee-trait-keen-scout-desc = One flight brings home up to three trays: the one the scout lands on and the ones next to it.
bee-trait-fertile = Fertile
bee-trait-fertile-desc = The queen lays more often and new bees cost less nectar, so the colony grows faster.
bee-trait-hardy = Hardy
bee-trait-hardy-desc = Bees taken out of the hive live half as long again.
bee-trait-mutagenic-pollination = Mutagenic pollination
bee-trait-mutagenic-pollination-desc = Each pollination works on the plant like 1u of unstable mutagen in the tray.
bee-trait-emergency-queen = Emergency queen
bee-trait-emergency-queen-desc = If the queen is lost, the colony raises a new, plain queen by itself.
bee-trait-lazy = Lazy
bee-trait-lazy-desc = Workers build combs more slowly.
bee-trait-gluttonous = Gluttonous
bee-trait-gluttonous-desc = Each comb eats more nectar.
bee-trait-frail = Frail
bee-trait-frail-desc = Bees taken out of the hive die sooner.
bee-trait-clumsy = Clumsy
bee-trait-clumsy-desc = Pollination doubles the harvest half as often.
bee-trait-sluggish = Sluggish
bee-trait-sluggish-desc = Each visit brings less nectar.
beekeeping-hive-emergency-queen = The colony has raised a new queen!

beekeeping-traits-none = none
beekeeping-queen-examine-traits = Traits: { $traits }.
beekeeping-queen-examine-slots = Trait slots: { $used } of { $slots } used.
beekeeping-queen-examine-payload = Reagents in the queen: { $amount }u of { $cap }.
beekeeping-bee-examine-traits = Colony traits: { $traits }.
beekeeping-queen-no-sting = The queen has nothing to carry this in: she has no sting.
beekeeping-queen-payload-lost = The queen lost her sting, and her reagents with it.
beekeeping-queen-trait-gained = The queen gained a trait: { $trait }.
beekeeping-queen-trait-lost = The queen lost a trait: { $trait }.
beekeeping-queen-trait-replaced = The queen gained { $trait } in place of { $old }.
beekeeping-queen-trait-nothing = The mutagen did nothing: the queen has no room for traits.

beekeeping-queen-hive-limit = The queen does not take: there are too many hives in the sector.
beekeeping-bee-bite = The bee bites your hand!
beekeeping-bee-sting = The bee stings your hand!

beekeeping-hive-examine-egg = A [color=yellow]queen egg[/color] lies in the hive. Collect the royal jelly into a container.
beekeeping-hive-examine-egg-dying = The queen egg will die soon.
beekeeping-verb-collect-jelly = Collect royal jelly
beekeeping-jelly-no-room = The container has no room.
beekeeping-jelly-collected = You collect the royal jelly.
beekeeping-queen-slot-added = The queen has room for more traits: { $slots } slots.
beekeeping-queen-slots-max = The queen already has as many slots as she can.

## Admin debug verbs

beekeeping-debug-fill-nectar = Hive: full nectar
beekeeping-debug-frames-honey = Hive: frames to honey
beekeeping-debug-frames-wax = Hive: frames to wax
beekeeping-debug-fill-bees = Hive: bees up to the limit
beekeeping-debug-restore-scout = Hive: bring the scout back
beekeeping-debug-scout-now = Hive: scout the whole area now
beekeeping-debug-scout-launch = Hive: send the scout out now
beekeeping-debug-forager-launch = Hive: send a forager
beekeeping-debug-egg = Hive: lay a queen egg
beekeeping-debug-egg-die = Hive: the egg dies
beekeeping-debug-speed-fast = Hive: run 10 times faster
beekeeping-debug-speed-normal = Hive: normal speed
beekeeping-debug-window = Hive: debug window

## Debug window and the beedebug command

beekeeping-debug-window-title = Hive debug
beekeeping-debug-window-gone = The hive is gone, or the server says nothing about it.
beekeeping-debug-section-colony = Colony
beekeeping-debug-section-trays = Known trays: { $count }
beekeeping-debug-queen = Queen
beekeeping-debug-outside = Bees outside
beekeeping-debug-pause = Pause
beekeeping-debug-scout = Scout
beekeeping-debug-scout-flying = flying
beekeeping-debug-scout-found = Trays seen
beekeeping-debug-next-trip = Scout trip
beekeeping-debug-next-forager = Forager trip
beekeeping-debug-foragers = Foragers in the air
beekeeping-debug-bees = Bees
beekeeping-debug-health = Hive health
beekeeping-debug-combs = Combs
beekeeping-debug-row-egg = Queen egg
beekeeping-debug-egg-there = yes, { $s } s left
beekeeping-debug-egg-cooldown = cooldown { $s } s
beekeeping-debug-egg-growing = coming in { $s } s
beekeeping-debug-section-queen = Queen
beekeeping-debug-no-queen-traits = no traits or no queen
beekeeping-debug-queen-line = { $slots } slots, payload { $payload }u
beekeeping-debug-pause-none = none
beekeeping-debug-pause-vacuum = no air
beekeeping-debug-pause-highpressure = high pressure
beekeeping-debug-pause-cold = too cold
beekeeping-debug-pause-heat = too hot
beekeeping-debug-pause-fire = fire
beekeeping-debug-scout-home = home
beekeeping-debug-scout-outside = outside
beekeeping-debug-scout-lost = dead
beekeeping-debug-section-outside = Bees outside: { $count }
beekeeping-debug-outside-none = All bees are home.
beekeeping-debug-outside-scout = Scout in hand
beekeeping-debug-outside-pollinator = Pollinator
beekeeping-debug-outside-lost = Lost forager
beekeeping-debug-outside-line = { $name }: gone in { $s } s
beekeeping-debug-nectar = Nectar
beekeeping-debug-yes = yes
beekeeping-debug-no = no
beekeeping-debug-seconds = in { $s } s
beekeeping-debug-state-ripe = Ripe plant
beekeeping-debug-state-unripe = Growing, no nectar yet
beekeeping-debug-state-inactive = No plant, too far or out of sight
beekeeping-debug-state-invalid = The tray is gone
beekeeping-debug-tray-line = { $tray }, { $distance } tiles from the hive, visit in { $visit } s
beekeeping-debug-legend = On the map: [color=limegreen]ripe[/color], [color=yellow]growing[/color], [color=red]empty or out of sight[/color], [color=cyan]a forager and its target[/color], the white line is the scout's flight. The white circle is the scout range.

cmd-beedebug-desc = Shows on the map what every hive knows: its known trays and forager flights.
cmd-beedebug-help = Usage: beedebug
cmd-beedebug-status = Hive debug on the map: { $status }.

## Apiary analyzer

apiary-analyzer-section-traits = Traits
apiary-analyzer-section-colony-traits = Colony traits
apiary-analyzer-section-stats = Characteristics
apiary-analyzer-section-colony = Colony
apiary-analyzer-section-frames = Frames ({ $count })
apiary-analyzer-section-trays = Known trays ({ $count })
apiary-analyzer-section-queen = Queen
apiary-analyzer-section-scout = Scout
apiary-analyzer-section-pollinator = Pollinator
apiary-analyzer-no-queen = There is no queen in the hive.
apiary-analyzer-stat-comb-rate = Comb speed
apiary-analyzer-stat-comb-cost = Nectar per comb
apiary-analyzer-stat-nectar = Nectar per visit
apiary-analyzer-stat-pollination = Chance to double a harvest
apiary-analyzer-stat-charges = Pollinations per bee
apiary-analyzer-stat-bee-cost = New bee cost
apiary-analyzer-stat-lay-rate = Laying speed
apiary-analyzer-stat-lifetime = Life outside the hive
apiary-analyzer-stat-scout-finds = Trays per flight
apiary-analyzer-stat-mutation = Mutation on pollination
apiary-analyzer-units = { $amount }u
apiary-analyzer-bees = Bees
apiary-analyzer-nectar = Nectar
apiary-analyzer-combs = Combs
apiary-analyzer-health = Integrity
apiary-analyzer-bees-home = Home
apiary-analyzer-new-bee = Brood
apiary-analyzer-queen = Queen
apiary-analyzer-egg = Egg
apiary-analyzer-yes = yes
apiary-analyzer-no = no
apiary-analyzer-frames-none = No frames.
apiary-analyzer-frame-combs = { $combs } of { $max } combs
apiary-analyzer-trays-none = The scout has not found any trays yet.
apiary-analyzer-tray-empty = empty tray
apiary-analyzer-tray-ripe = The plant is ripe, the workers visit it.
apiary-analyzer-tray-growing = The plant is still growing.
apiary-analyzer-tray-blind = The tray is empty or the workers cannot reach it now.
apiary-analyzer-of = { $value } of { $max }
apiary-analyzer-row-slots = Trait slots
apiary-analyzer-row-payload = Reagents, u
apiary-analyzer-row-hive = Hive
apiary-analyzer-hive-known = intact
apiary-analyzer-hive-lost = destroyed
apiary-analyzer-row-memory = Trays remembered
apiary-analyzer-row-charges = Pollinations left
beekeeping-take-queen-done = You carefully take the queen out.
apiary-analyzer-trait-separator = {": "}
beekeeping-queen-examine-dying = [color=red]Away from a hive the queen weakens and will die soon.[/color]
beekeeping-queen-died = The queen died away from a hive.
beekeeping-frame-upgraded = You build the frame up with wax. It now holds { $max } combs.
beekeeping-frame-upgrade-max = The frame already holds { $max } combs, it cannot take more.
beekeeping-flatpack-examine = Use a [color=yellow]crowbar[/color] to unpack this.
apiary-analyzer-nectar-idle =
    Nectar [color=#9A9A9A]is not spent[/color]: the hive has no queen.
    Put a [bold]queen[/bold] in and the hive comes alive.
apiary-analyzer-nectar-bees =
    Nectar [color=#FFA500]hardly piles up[/color]: it is [bold]spent at once[/bold].
    Right now it waits for [color=#FFD700]new bees[/color], combs have no room.
apiary-analyzer-nectar-beesandcombs =
    Nectar [color=#FFA500]hardly piles up[/color]: it is [bold]spent at once[/bold].
    The queen lays [color=#FFD700]bees[/color], the rest goes into [color=#F0E68C]combs[/color].
apiary-analyzer-nectar-combs =
    Nectar [color=#FFA500]hardly piles up[/color]: it is [bold]spent at once[/bold].
    Right now it goes into [color=#F0E68C]combs[/color].
apiary-analyzer-nectar-framesfull =
    Nectar [color=#FFA500]piles up[/color]: there is nothing to spend it on.
    Every frame is full, [bold]cut the combs[/bold].
apiary-analyzer-nectar-noworkers =
    Nectar [color=#FFA500]piles up[/color]: nobody is home to build combs.
    Every worker is out.
apiary-analyzer-foraging = Flying
apiary-analyzer-nectar-noframes =
    Nectar [color=#FFA500]piles up[/color]: there is nothing to spend it on.
    The hive has [bold]no frames[/bold], put some in.
beekeeping-take-scout-tired = The scout is resting after her trip. Try again later.
beekeeping-take-workers-tired = Every free worker is resting after work. Try again later.
beekeeping-debug-forager-sent = A forager flew off to a tray.
beekeeping-debug-forager-no-trays = The hive knows no trays: send the scout first.
beekeeping-debug-forager-too-many = { $max } foragers are already out, the hive lets out no more.
beekeeping-debug-forager-no-workers = No free workers are home.
beekeeping-debug-forager-failed = The forager could not fly off.
apiary-analyzer-brood-going = yes
apiary-analyzer-brood-stopped = no
apiary-analyzer-stat-egg-wait = Egg wait
apiary-analyzer-stat-egg-life = Egg life
apiary-analyzer-stat-egg-cooldown = Egg cooldown
bee-trait-early-egg = Early egg
bee-trait-early-egg-desc = A full colony lays the queen's egg sooner.
bee-trait-hardy-egg = Hardy egg
bee-trait-hardy-egg-desc = The queen's egg lives longer, more time to collect the jelly.
bee-trait-swift-queen = Swift queen
bee-trait-swift-queen-desc = A new egg comes sooner after the last one.
bee-trait-lazy-queen = Lazy queen
bee-trait-lazy-queen-desc = The queen's egg comes much later.
bee-trait-fragile-egg = Fragile egg
bee-trait-fragile-egg-desc = The queen's egg lives half as long, collect the jelly fast.
apiary-analyzer-row-instability = Instability