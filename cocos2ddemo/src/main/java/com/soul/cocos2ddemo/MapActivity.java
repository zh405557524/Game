package com.soul.cocos2ddemo;

import android.os.Bundle;

import org.cocos2d.layers.CCLayer;
import org.cocos2d.layers.CCScene;
import org.cocos2d.nodes.CCDirector;
import org.cocos2d.opengl.CCGLSurfaceView;

/**
 * 地图制作
 */
public class MapActivity extends BaseActivity {

    private CCDirector mCcDirector;

    @Override
    public void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        //1 创建 CCGLSurfaceView
        CCGLSurfaceView ccglSurfaceView = new CCGLSurfaceView(this);
        //2 创建CCDirector
        mCcDirector = CCDirector.sharedDirector();

        //2-1 CCDirector开启一个线程，
        mCcDirector.attachInView(ccglSurfaceView);

        //3 director 常规设置
        mCcDirector.setDeviceOrientation(CCDirector.kCCDeviceOrientationLandscapeLeft);
        mCcDirector.setDisplayFPS(true);
        mCcDirector.setScreenSize(480,320);
        //4 创建 CCScene
        CCScene ccScene = CCScene.node();
        //5 创建CCLayer
        CCLayer ccLayer= new DemoLayer();
        //6 CCLayer添加到CCScene

        ccScene.addChild(ccLayer);

        //7 ccDirector 运行ccScene
        mCcDirector.runWithScene(ccScene);


        setContentView(ccglSurfaceView);

    }


    @Override
    protected void onResume() {
        super.onResume();
        mCcDirector.resume();
    }

    @Override
    protected void onPause() {
        super.onPause();
        mCcDirector.pause();
    }

    @Override
    protected void onDestroy() {
        super.onDestroy();
        mCcDirector.end();
    }
}
