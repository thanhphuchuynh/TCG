import AppKit
import ImageIO
let destination = URL(fileURLWithPath: CommandLine.arguments[1])
try FileManager.default.createDirectory(at: destination, withIntermediateDirectories: true)
for size in [16,32,128,256,512] {
    for scale in [1,2] {
        let pixels = size * scale
        let image = NSImage(size:NSSize(width:1024,height:1024))
        image.lockFocus()
        NSColor(calibratedRed:0.12,green:0.34,blue:0.62,alpha:1).setFill()
        NSBezierPath(roundedRect:NSRect(x:60,y:60,width:904,height:904),xRadius:210,yRadius:210).fill()
        NSColor(calibratedRed:1,green:0.76,blue:0.23,alpha:1).setFill()
        NSBezierPath(roundedRect:NSRect(x:240,y:210,width:470,height:650),xRadius:48,yRadius:48).fill()
        NSColor.white.setFill()
        NSBezierPath(roundedRect:NSRect(x:315,y:155,width:470,height:650),xRadius:48,yRadius:48).fill()
        NSColor(calibratedRed:0.18,green:0.48,blue:0.75,alpha:1).setFill()
        NSBezierPath(roundedRect:NSRect(x:360,y:360,width:380,height:395),xRadius:24,yRadius:24).fill()
        NSColor(calibratedRed:1,green:0.76,blue:0.23,alpha:1).setFill()
        let star = NSBezierPath()
        for point in 0..<10 {
            let angle = Double(point) * Double.pi / 5 + Double.pi / 2
            let radius = point % 2 == 0 ? 125.0 : 55.0
            let p = NSPoint(x:550 + cos(angle)*radius, y:560 + sin(angle)*radius)
            if point == 0 { star.move(to:p) } else { star.line(to:p) }
        }
        star.close(); star.fill()
        NSColor(calibratedWhite:0.8,alpha:1).setFill()
        NSBezierPath(roundedRect:NSRect(x:375,y:265,width:300,height:24),xRadius:12,yRadius:12).fill()
        NSBezierPath(roundedRect:NSRect(x:375,y:215,width:200,height:24),xRadius:12,yRadius:12).fill()
        image.unlockFocus()
        let bitmap = NSBitmapImageRep(bitmapDataPlanes:nil,pixelsWide:pixels,pixelsHigh:pixels,bitsPerSample:8,samplesPerPixel:4,hasAlpha:true,isPlanar:false,colorSpaceName:.deviceRGB,bytesPerRow:0,bitsPerPixel:0)!
        NSGraphicsContext.saveGraphicsState(); NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep:bitmap)
        image.draw(in:NSRect(x:0,y:0,width:pixels,height:pixels)); NSGraphicsContext.restoreGraphicsState()
        let name = "icon_\(size)x\(size)" + (scale == 2 ? "@2x" : "") + ".png"
        try bitmap.representation(using:.png,properties:[:])!.write(to:destination.appendingPathComponent(name))
    }
}
